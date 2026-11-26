using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The measured outline of every aircraft model installed on THIS PC, by the model title the simulator
/// reports for a traffic aircraft (its <c>[FLTSIM.N] title</c>). Nothing here is a list anyone maintains:
/// <list type="bullet">
/// <item>the index is read from the installed packages' own <c>aircraft.cfg</c> files, and read again
/// (at most every <see cref="ReindexMinutes"/>) when the simulator reports a title it does not hold, so a
/// package installed or updated while the app runs is picked up;</item>
/// <item>a model is measured the first time one of its titles is seen (<see cref="GroundTrafficModelReader"/>)
/// and the result kept on disk under the model file's path, size and date, so a livery added to a
/// known model costs nothing and a changed model file is measured again by itself.</item>
/// </list>
/// <see cref="TryGet"/> never blocks: it answers from memory and queues the rest for one background
/// thread. A model that cannot be read (a streamed or encrypted package, a title no package declares)
/// answers null for good, and the monitor falls back to the type's published size.
/// </summary>
public sealed partial class GroundTrafficModelCatalog : IDisposable
{
    /// <summary>A title no package declares is looked for again after this long, once the packages are re-read.</summary>
    public const int ReindexMinutes = 5;

    /// <summary>Bumped when the measurement changes, so every cached outline is measured again.</summary>
    public const int CacheVersion = 1;

    private const string CacheFileName = "outlines.json";
    private const int MaxScanDepth = 6;

    /// <summary>Where one title's model lives: its aircraft folder, its <c>model=</c> suffix, and the folder its <c>base_container</c> names.</summary>
    internal readonly record struct ModelRef(string AircraftDir, string ModelSuffix, string? BaseDir);

    private static readonly LogChannel _log = Log.Channel("ground_traffic");

    private readonly Func<string?> _packagesRoot;
    private readonly string? _cacheDir;
    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    // title → its outline, or null when its model cannot be measured.
    private readonly ConcurrentDictionary<string, AircraftOutline?> _byTitle = new(StringComparer.OrdinalIgnoreCase);
    // title → when a title no package declared may be looked for again.
    private readonly ConcurrentDictionary<string, DateTime> _unknownUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<string> _queue = new();
    // model file key → outline (null = unmeasurable). Worker thread only.
    private Dictionary<string, AircraftOutline?> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ModelRef>? _index;
    private DateTime _indexedUtc = DateTime.MinValue;
    private string _indexStamp = "";
    private bool _cacheDirty;
    private Thread? _worker;
    private volatile bool _disposed;

    /// <param name="cacheDir">Where measured outlines are kept between runs; null keeps them in memory only.</param>
    /// <param name="packagesRoot">The simulator's packages folder; defaults to the one <c>UserCfg.opt</c> names.</param>
    public GroundTrafficModelCatalog(string? cacheDir, Func<string?>? packagesRoot = null, Func<DateTime>? utcNow = null)
    {
        _cacheDir = cacheDir;
        _packagesRoot = packagesRoot ?? AircraftCfgCatalog.FindInstalledPackagesPath;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// The measured outline of the model with this title, or null: not measured yet (it is queued, and a
    /// later call answers), or not measurable. Never blocks, never throws.
    /// </summary>
    public AircraftOutline? TryGet(string? title)
    {
        if (_disposed || string.IsNullOrWhiteSpace(title)) return null;
        title = title.Trim();
        if (_byTitle.TryGetValue(title, out var known)) return known;
        if (_unknownUntil.TryGetValue(title, out var until) && _utcNow() < until) return null;
        lock (_lock)
        {
            if (_disposed || !_queued.Add(title)) return null;
            if (_worker == null)
            {
                _worker = new Thread(Work) { IsBackground = true, Name = "GroundTrafficModelCatalog", Priority = ThreadPriority.BelowNormal };
                _worker.Start();
            }
        }
        try { _queue.Add(title); } catch (InvalidOperationException) { /* disposed meanwhile */ }
        return null;
    }

    /// <summary>True once <paramref name="title"/> has an answer, measured or not (tests and the audit tool).</summary>
    internal bool IsSettled(string title)
        => _byTitle.ContainsKey(title.Trim()) || (_unknownUntil.TryGetValue(title.Trim(), out var until) && _utcNow() < until);

    private void Work()
    {
        try
        {
            LoadCache();
            foreach (string title in _queue.GetConsumingEnumerable())
            {
                try { Settle(title); }
                catch (Exception ex) { _log.Info($"ev=model error title=\"{title}\" msg=\"{ex.Message}\""); _byTitle[title] = null; }
                lock (_lock) _queued.Remove(title);
                if (_queue.Count == 0 && _cacheDirty) SaveCache();
            }
        }
        catch (Exception) { /* the catalog is an optimisation: never take the app down */ }
    }

    private void Settle(string title)
    {
        var now = _utcNow();
        if (_index == null || (!_index.ContainsKey(title) && (now - _indexedUtc).TotalMinutes >= ReindexMinutes))
        {
            // A title no package declares is common (the simulator's own streamed traffic), so the packages
            // are read again only when their folders have changed since the last read.
            string? root = _packagesRoot();
            string stamp = root != null ? PackagesStamp(root) : "";
            if (_index == null || stamp != _indexStamp)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                _index = root != null ? BuildIndex(root) : new Dictionary<string, ModelRef>(StringComparer.OrdinalIgnoreCase);
                _indexStamp = stamp;
                _log.Info($"ev=model-index titles={_index.Count} ms={clock.ElapsedMilliseconds}");
            }
            _indexedUtc = now;
        }
        if (!_index.TryGetValue(title, out var model))
        {
            _unknownUntil[title] = now.AddMinutes(ReindexMinutes);
            _log.Info($"ev=model title=\"{title}\" result=not-installed");
            return;
        }
        _unknownUntil.TryRemove(title, out _);
        var parts = Parts(model);
        if (parts.Count == 0)
        {
            _byTitle[title] = null;
            _log.Info($"ev=model title=\"{title}\" result=no-model-file");
            return;
        }
        string key = PartsKey(parts);
        if (!_byFile.TryGetValue(key, out var outline))
        {
            outline = GroundTrafficModelReader.Measure(parts, out string why);
            _byFile[key] = outline;
            _cacheDirty = true;
            string file = Path.GetFileName(parts[^1].Lods[0]);
            _log.Info(outline == null
                ? $"ev=model title=\"{title}\" result=unreadable why=\"{why}\" parts={parts.Count} file=\"{file}\""
                : FormattableString.Invariant($"ev=model title=\"{title}\" result=measured nose={outline.NoseM:0.0} tail={outline.TailM:0.0} halfSpan={outline.HalfSpanM:0.0} parts={parts.Count} file=\"{file}\""));
        }
        _byTitle[title] = outline;
    }

    // A model's identity: its one file's, or for an assembled model a digest of every part's.
    internal static string PartsKey(IReadOnlyList<GroundTrafficModelReader.ModelPart> parts)
    {
        if (parts.Count == 1) return FileKey(parts[0].Lods[0]);
        var all = new System.Text.StringBuilder();
        foreach (var part in parts) all.Append(FileKey(part.Lods[0])).Append('>').Append(part.Parent).Append('@').Append(part.AttachNode).Append('\n');
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(all.ToString()));
        return parts[0].Lods[0] + "|" + parts.Count + " parts|" + Convert.ToHexString(hash, 0, 12);
    }

    // What the packages folders hold, cheaply: every package folder's name and date. A package installed,
    // removed or updated changes it; nothing inside a package is read.
    internal static string PackagesStamp(string packagesRoot)
    {
        var all = new System.Text.StringBuilder();
        foreach (string folder in Database.MsfsPackageLayout.PackageFolders(packagesRoot))
            foreach (string package in SafeDirectories(folder))
            {
                all.Append(package).Append('|');
                try { all.Append(Directory.GetLastWriteTimeUtc(package).Ticks); } catch (Exception) { }
                all.Append('\n');
            }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(all.ToString())));
    }

    // A model file's identity: a changed or replaced file is measured again.
    private static string FileKey(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return FormattableString.Invariant($"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception) { return path; }
    }

    // ── The index: title → model, from every installed aircraft.cfg ────────────────────────────────

    /// <summary>Every title the installed packages declare, with where its model lives. Never throws.</summary>
    internal static Dictionary<string, ModelRef> BuildIndex(string packagesRoot)
    {
        var index = new Dictionary<string, ModelRef>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        foreach (string folder in Database.MsfsPackageLayout.PackageFolders(packagesRoot))
        {
            foreach (string package in SafeDirectories(folder))
            {
                if (Directory.Exists(Path.Combine(package, "SimObjects"))) roots.Add(package);
                string airplanes = Path.Combine(package, "SimObjects", "Airplanes");
                if (!Directory.Exists(airplanes)) continue;
                foreach (string cfg in AircraftCfgs(airplanes, 0))
                {
                    try { AddTitles(index, cfg, File.ReadAllLines(cfg)); }
                    catch (Exception) { /* unreadable: its titles stay unknown */ }
                }
            }
        }
        VfsRoots = roots;
        return index;
    }

    /// <summary>Every installed package that holds SimObjects, as of the last <see cref="BuildIndex"/>: where <see cref="Vfs"/> looks.</summary>
    internal static IReadOnlyList<string> VfsRoots { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// A path as the simulator's virtual file system sees it: the packages overlay one another, so a file
    /// one package names inside its own tree may live at the same relative path in another (the Fenix's
    /// presets name a fuselage that its common package holds; a livery package's
    /// <c>model = "\..\..\COWS_DA40NG\model.BOOM"</c> names the base aircraft's folder). The path itself
    /// when it exists, else the first package that holds it, else the path unchanged.
    /// </summary>
    internal static string Vfs(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path)) return path;
            if (PackageRootOf(path) is not { } own) return path;
            string relative = Path.GetRelativePath(own, path);
            foreach (string root in VfsRoots)
            {
                string candidate = Path.Combine(root, relative);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
            }
        }
        catch (Exception) { /* an odd path: as it came */ }
        return path;
    }

    /// <summary>One aircraft.cfg's titles: each <c>[FLTSIM.N]</c> gives a title and a model suffix; <c>[VARIATION] base_container</c> names the folder a livery borrows its model from.</summary>
    internal static void AddTitles(Dictionary<string, ModelRef> index, string cfgPath, IReadOnlyList<string> lines)
    {
        string dir = Path.GetDirectoryName(cfgPath) ?? "";
        string section = "";
        string? baseDir = null, title = null, suffix = null;
        var found = new List<(string Title, string Suffix)>();
        void Close()
        {
            if (title != null) found.Add((title, suffix ?? ""));
            title = suffix = null;
        }
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line[0] == '[')
            {
                Close();
                int end = line.IndexOf(']');
                section = end > 0 ? line[1..end].Trim().ToUpperInvariant() : "";
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim().ToLowerInvariant();
            string value = Unquote(line[(eq + 1)..]);
            if (section == "VARIATION" && key == "base_container" && value.Length > 0)
                baseDir = Path.GetFullPath(Path.Combine(dir, value.Replace('/', Path.DirectorySeparatorChar)));
            else if (section.StartsWith("FLTSIM.", StringComparison.Ordinal))
            {
                if (key == "title") title = value;
                else if (key == "model") suffix = value;
            }
        }
        Close();
        foreach (var (t, s) in found)
            if (t.Length > 0) index.TryAdd(t, new ModelRef(dir, s, baseDir));   // the first package to name a title keeps it
    }

    // A value without its quotes or its trailing comment: "x" ; note → x.
    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length > 0 && value[0] == '"')
        {
            int close = value.IndexOf('"', 1);
            return close > 0 ? value[1..close].Trim() : value[1..].Trim();
        }
        int comment = value.IndexOf(';');
        if (comment >= 0) value = value[..comment];
        comment = value.IndexOf("//", StringComparison.Ordinal);
        if (comment >= 0) value = value[..comment];
        return value.Trim();
    }

    [GeneratedRegex(@"ModelFile\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelFileAttribute();

    /// <summary>
    /// The model's glTF files, most detailed first: the levels of detail its <c>model.cfg</c> names through
    /// its model XML, looked for in the aircraft's own model folder and then in its base container's.
    /// Empty when none is on disk.
    /// </summary>
    internal static IReadOnlyList<string> LodPaths(ModelRef model)
    {
        string folder = model.ModelSuffix.Length > 0 ? "model." + model.ModelSuffix : "model";
        foreach (string? root in new[] { model.AircraftDir, model.BaseDir })
        {
            if (root == null) continue;
            string modelDir;
            try { modelDir = Vfs(Path.GetFullPath(Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)))); }
            catch (Exception) { continue; }
            if (!Directory.Exists(modelDir)) continue;
            var lods = LodsOf(modelDir);
            if (lods.Count > 0) return lods;
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// The parts a title's model is made of: the one model for an ordinary aircraft; for an MSFS 2024
    /// MODULAR aircraft (its <c>aircraft.cfg</c> sits beside an <c>attached_objects.cfg</c>, in
    /// <c>presets/&lt;maker&gt;/&lt;preset&gt;/config</c>) the exterior it is assembled from: the common model,
    /// then every <c>[SIM_ATTACHMENT.N]</c> whose <c>attach_to_model</c> is the exterior, and the attachments
    /// of those in turn. Empty when nothing is on disk.
    /// </summary>
    internal static IReadOnlyList<GroundTrafficModelReader.ModelPart> Parts(ModelRef model)
    {
        var lods = LodPaths(model);
        if (lods.Count > 0) return new[] { new GroundTrafficModelReader.ModelPart(lods) };
        string attached = Path.Combine(model.AircraftDir, "attached_objects.cfg");
        if (!File.Exists(attached)) return Array.Empty<GroundTrafficModelReader.ModelPart>();

        var parts = new List<GroundTrafficModelReader.ModelPart>();
        try
        {
            string? packageRoot = PackageRootOf(model.AircraftDir);
            if (packageRoot == null) return parts;
            // presets/<maker>/<preset>/config → the aircraft's own folder, four levels up: its common model.
            string aircraftRoot = Path.GetFullPath(Path.Combine(model.AircraftDir, "..", "..", "..", ".."));
            var common = LodsOf(Vfs(Path.Combine(aircraftRoot, "common", "model")));
            int parent = -1;
            if (common.Count > 0) { parts.Add(new GroundTrafficModelReader.ModelPart(common)); parent = 0; }
            AddAttachments(parts, attached, packageRoot, parent, depth: 0);
        }
        catch (Exception) { /* unreadable: whatever was gathered */ }
        // The common model alone is a helper with no airframe: with no attachment there is nothing to measure.
        return parts.Any(p => p.Parent >= 0 || parts.Count > 1 && p != parts[0]) || (parts.Count == 1 && parts[0].Parent == -1 && !IsCommonOnly(parts, model))
            ? parts : Array.Empty<GroundTrafficModelReader.ModelPart>();
    }

    // True when the one part gathered is the aircraft's common model and nothing was attached to it.
    private static bool IsCommonOnly(List<GroundTrafficModelReader.ModelPart> parts, ModelRef model)
    {
        string aircraftRoot = Path.GetFullPath(Path.Combine(model.AircraftDir, "..", "..", "..", ".."));
        string common = Path.Combine(aircraftRoot, "common", "model") + Path.DirectorySeparatorChar;
        return parts.Count == 1 && parts[0].Lods[0].StartsWith(common, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An attachment whose folder name matches is not airframe: evacuation slides, covers, ground equipment, people.</summary>
    [GeneratedRegex(@"slide|cover|chock|ground|gpu|stairs|pushback|catering|baggage|jetway|marshal|pilot|crew|passenger|human|interior|cabin|cockpit|cast.?shadow|lod_?friend",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotAirframeAttachment();

    private const int MaxAttachmentDepth = 4;

    private static void AddAttachments(List<GroundTrafficModelReader.ModelPart> parts, string attachedCfg, string packageRoot, int parent, int depth)
    {
        if (depth > MaxAttachmentDepth || parts.Count > 400) return;
        string section = "";
        string? root = null, file = null, toModel = null, toNode = null;
        void Close()
        {
            if (section.StartsWith("SIM_ATTACHMENT.", StringComparison.Ordinal) && root != null && file != null
                && string.Equals(toModel, "exterior", StringComparison.OrdinalIgnoreCase)
                && !NotAirframeAttachment().IsMatch(Path.GetFileName(root.TrimEnd('/', '\\')))
                && !NotAirframeAttachment().IsMatch(Path.GetFileName(file.Replace('\\', '/'))))
            {
                string attachmentDir = Path.GetFullPath(Path.Combine(packageRoot, root.Replace('/', Path.DirectorySeparatorChar)));
                var lods = LodsFromModelFile(Vfs(Path.GetFullPath(Path.Combine(attachmentDir, file.Replace('/', Path.DirectorySeparatorChar)))), attachmentDir);
                // An attachment with no geometry of its own still carries the parts attached to it.
                int self = parent;
                if (lods.Count > 0)
                {
                    parts.Add(new GroundTrafficModelReader.ModelPart(lods, parent, toNode));
                    self = parts.Count - 1;
                }
                string nested = Vfs(Path.Combine(attachmentDir, "config", "attached_objects.cfg"));
                if (File.Exists(nested)) AddAttachments(parts, nested, packageRoot, self, depth + 1);
            }
            root = file = toModel = toNode = null;
        }
        foreach (string raw in File.ReadAllLines(attachedCfg))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line[0] == '[')
            {
                Close();
                int end = line.IndexOf(']');
                section = end > 0 ? line[1..end].Trim().ToUpperInvariant() : "";
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string value = Unquote(line[(eq + 1)..]);
            switch (line[..eq].Trim().ToLowerInvariant())
            {
                case "attachment_root": root = value; break;
                case "attachment_file": file = value; break;
                case "attach_to_model": toModel = value; break;
                case "attach_to_node": toNode = value; break;
            }
        }
        Close();
    }

    // The package a folder belongs to: the parent of its "SimObjects" folder, which attachment paths start from.
    private static string? PackageRootOf(string dir)
    {
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            if (string.Equals(d.Name, "SimObjects", StringComparison.OrdinalIgnoreCase)) return d.Parent?.FullName;
        return null;
    }

    private static List<string> LodsOf(string modelDir)
    {
        var lods = new List<string>();
        try
        {
            string cfg = Path.Combine(modelDir, "model.cfg");
            if (File.Exists(cfg))
            {
                bool inModels = false;
                foreach (string raw in File.ReadAllLines(cfg))
                {
                    string line = raw.Trim();
                    if (line.StartsWith('[')) { inModels = line.StartsWith("[models]", StringComparison.OrdinalIgnoreCase); continue; }
                    int eq = line.IndexOf('=');
                    if (!inModels || eq <= 0) continue;
                    string key = line[..eq].Trim().ToLowerInvariant();
                    if (key != "normal" && key != "exterior") continue;
                    lods.AddRange(LodsFromModelFile(
                        Vfs(Path.GetFullPath(Path.Combine(modelDir, Unquote(line[(eq + 1)..]).Replace('/', Path.DirectorySeparatorChar)))), modelDir));
                    if (lods.Count > 0) break;
                }
            }
            if (lods.Count == 0 && Directory.Exists(modelDir)) lods.AddRange(GltfsIn(modelDir));
        }
        catch (Exception) { /* unreadable: no model */ }
        return lods;
    }

    // The levels of detail a model file names: itself when it is a glTF, the files its model XML lists
    // (relative to the XML), else the glTF files beside the XML.
    private static List<string> LodsFromModelFile(string file, string fallbackDir)
    {
        var lods = new List<string>();
        try
        {
            if (file.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)) { if (File.Exists(file)) lods.Add(file); }
            else if (File.Exists(file))
            {
                string xmlDir = Path.GetDirectoryName(file) ?? fallbackDir;
                foreach (Match m in ModelFileAttribute().Matches(File.ReadAllText(file)))
                {
                    string lod = Vfs(Path.GetFullPath(Path.Combine(xmlDir, m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))));
                    if (File.Exists(lod) && !lods.Contains(lod, StringComparer.OrdinalIgnoreCase)) lods.Add(lod);
                }
                if (lods.Count == 0) lods.AddRange(GltfsIn(xmlDir));
            }
        }
        catch (Exception) { /* unreadable: no model */ }
        return lods;
    }

    private static IEnumerable<string> GltfsIn(string dir)
    {
        try
        {
            var files = Directory.GetFiles(dir, "*.gltf");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }
        catch (Exception) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> AircraftCfgs(string dir, int depth)
    {
        string[] here;
        try { here = Directory.GetFiles(dir, "aircraft.cfg"); } catch (Exception) { here = Array.Empty<string>(); }
        foreach (string f in here) yield return f;
        if (depth >= MaxScanDepth) yield break;
        foreach (string sub in SafeDirectories(dir))
            foreach (string f in AircraftCfgs(sub, depth + 1))
                yield return f;
    }

    private static string[] SafeDirectories(string path)
    {
        try { return Directory.GetDirectories(path); } catch (Exception) { return Array.Empty<string>(); }
    }

    // ── The cache: measured outlines by model file, kept between runs ───────────────────────────────

    private sealed class CacheFile
    {
        public int Version { get; set; }
        public List<CacheEntry> Models { get; set; } = new();
    }

    private sealed class CacheEntry
    {
        public string Key { get; set; } = "";
        public bool Measured { get; set; }
        public double TailM { get; set; }
        public double NoseM { get; set; }
        public float[] HalfWidths { get; set; } = Array.Empty<float>();
    }

    private void LoadCache()
    {
        if (_cacheDir == null) return;
        try
        {
            string path = Path.Combine(_cacheDir, CacheFileName);
            if (!File.Exists(path)) return;
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path));
            if (file == null || file.Version != CacheVersion) return;
            foreach (var e in file.Models)
                _byFile[e.Key] = e.Measured ? new AircraftOutline(e.TailM, e.NoseM, e.HalfWidths) : null;
        }
        catch (Exception ex)
        {
            _byFile = new Dictionary<string, AircraftOutline?>(StringComparer.OrdinalIgnoreCase);
            Log.Warn("GroundTraffic", $"rebuilding {CacheFileName}: {ex.Message}");
        }
    }

    private void SaveCache()
    {
        _cacheDirty = false;
        if (_cacheDir == null) return;
        try
        {
            var file = new CacheFile { Version = CacheVersion };
            foreach (var (key, outline) in _byFile)
                file.Models.Add(outline == null
                    ? new CacheEntry { Key = key }
                    : new CacheEntry { Key = key, Measured = true, TailM = outline.TailM, NoseM = outline.NoseM, HalfWidths = outline.HalfWidths.ToArray() });
            string path = Path.Combine(_cacheDir, CacheFileName), tmp = path + ".tmp";
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(tmp, JsonSerializer.Serialize(file));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { Log.Warn("GroundTraffic", $"could not write {CacheFileName}: {ex.Message}"); }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { }
    }
}
