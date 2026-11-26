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
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string? root = _packagesRoot();
            _index = root != null ? BuildIndex(root) : new Dictionary<string, ModelRef>(StringComparer.OrdinalIgnoreCase);
            _indexedUtc = now;
            _log.Info($"ev=model-index titles={_index.Count} ms={clock.ElapsedMilliseconds}");
        }
        if (!_index.TryGetValue(title, out var model))
        {
            _unknownUntil[title] = now.AddMinutes(ReindexMinutes);
            _log.Info($"ev=model title=\"{title}\" result=not-installed");
            return;
        }
        _unknownUntil.TryRemove(title, out _);
        var lods = LodPaths(model);
        if (lods.Count == 0)
        {
            _byTitle[title] = null;
            _log.Info($"ev=model title=\"{title}\" result=no-model-file");
            return;
        }
        string key = FileKey(lods[0]);
        if (!_byFile.TryGetValue(key, out var outline))
        {
            outline = GroundTrafficModelReader.Measure(lods);
            _byFile[key] = outline;
            _cacheDirty = true;
            _log.Info(outline == null
                ? $"ev=model title=\"{title}\" result=unreadable file=\"{Path.GetFileName(lods[0])}\""
                : FormattableString.Invariant($"ev=model title=\"{title}\" result=measured nose={outline.NoseM:0.0} tail={outline.TailM:0.0} halfSpan={outline.HalfSpanM:0.0} file=\"{Path.GetFileName(lods[0])}\""));
        }
        _byTitle[title] = outline;
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
        foreach (string folder in Database.MsfsPackageLayout.PackageFolders(packagesRoot))
        {
            foreach (string package in SafeDirectories(folder))
            {
                string airplanes = Path.Combine(package, "SimObjects", "Airplanes");
                if (!Directory.Exists(airplanes)) continue;
                foreach (string cfg in AircraftCfgs(airplanes, 0))
                {
                    try { AddTitles(index, cfg, File.ReadAllLines(cfg)); }
                    catch (Exception) { /* unreadable: its titles stay unknown */ }
                }
            }
        }
        return index;
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
            string modelDir = Path.Combine(root, folder);
            if (!Directory.Exists(modelDir)) continue;
            var lods = LodsOf(modelDir);
            if (lods.Count > 0) return lods;
        }
        return Array.Empty<string>();
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
                    string file = Path.GetFullPath(Path.Combine(modelDir, Unquote(line[(eq + 1)..]).Replace('/', Path.DirectorySeparatorChar)));
                    if (file.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)) { if (File.Exists(file)) lods.Add(file); }
                    else if (File.Exists(file))
                    {
                        string xmlDir = Path.GetDirectoryName(file) ?? modelDir;
                        foreach (Match m in ModelFileAttribute().Matches(File.ReadAllText(file)))
                        {
                            string lod = Path.GetFullPath(Path.Combine(xmlDir, m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar)));
                            if (File.Exists(lod) && !lods.Contains(lod, StringComparer.OrdinalIgnoreCase)) lods.Add(lod);
                        }
                        // A model XML that names no file: the glTF files beside it.
                        if (lods.Count == 0) lods.AddRange(GltfsIn(xmlDir));
                    }
                    if (lods.Count > 0) break;
                }
            }
            if (lods.Count == 0) lods.AddRange(GltfsIn(modelDir));
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
