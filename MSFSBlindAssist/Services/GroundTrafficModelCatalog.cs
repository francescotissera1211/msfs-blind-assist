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
    /// <param name="Icao">The type its aircraft.cfg declares (<c>icao_type_designator</c>), upper case; "" for none.</param>
    /// <param name="AirlineCode">Its livery's <c>icao_airline</c>, upper case; "" for none.</param>
    /// <param name="Airline">Its livery's <c>atc_airline</c>, the name the simulator reports as ATC AIRLINE; "" for none.</param>
    internal readonly record struct ModelRef(string AircraftDir, string ModelSuffix, string? BaseDir,
        string Icao = "", string AirlineCode = "", string Airline = "");

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
    // Written by the worker thread only, each published whole: the traffic monitor reads them from its own.
    private volatile Dictionary<string, ModelRef>? _index;
    private volatile Dictionary<string, string> _airlines = new(StringComparer.OrdinalIgnoreCase);
    private volatile int _indexVersion;
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
        Enqueue(title);
        return null;
    }

    // Hands the worker a title to settle (or IndexOnly), starting it on the first one. Never blocks.
    private void Enqueue(string title)
    {
        lock (_lock)
        {
            if (_disposed || !_queued.Add(title)) return;
            if (_worker == null)
            {
                _worker = new Thread(Work) { IsBackground = true, Name = "GroundTrafficModelCatalog", Priority = ThreadPriority.BelowNormal };
                _worker.Start();
            }
        }
        try { _queue.Add(title); } catch (InvalidOperationException) { /* disposed meanwhile */ }
    }

    // Asks the worker to read the packages without measuring anything: what they DECLARE is wanted by
    // callers that never ask for an outline (the traffic window names airborne traffic too).
    private const string IndexOnly = "\u0001index";
    private int _indexAsked;
    private void AskForIndex()
    {
        if (_disposed || _index != null || Interlocked.Exchange(ref _indexAsked, 1) == 1) return;
        Enqueue(IndexOnly);
    }

    /// <summary>
    /// Counts the times the installed packages have been read: 0 until the first read. What the packages
    /// DECLARE (<see cref="Declared"/>, <see cref="AirlineName"/>) is known from then on, and may differ
    /// after each later read, so a name built from it is built again when this moves.
    /// </summary>
    public int IndexVersion => _indexVersion;

    /// <summary>
    /// What the package that owns <paramref name="title"/> declares for it in its aircraft.cfg: its ICAO
    /// type designator and its livery's airline code. Empty strings for a title no package declares, and
    /// until the packages have been read (asking here has them read, in the background).
    /// </summary>
    public (string Icao, string AirlineCode) Declared(string? title)
    {
        var index = _index;
        if (index == null) AskForIndex();
        return index != null && !string.IsNullOrWhiteSpace(title) && index.TryGetValue(title.Trim(), out var model)
            ? (model.Icao, model.AirlineCode) : ("", "");
    }

    /// <summary>
    /// The name the installed liveries give an airline, by its three-letter ICAO code ("RYR" → "Ryanair"):
    /// learnt from every livery that declares both (<see cref="AirlineNames"/>), so nothing here is a list
    /// anyone maintains. "" for a code no installed livery names.
    /// </summary>
    public string AirlineName(string code)
        => !string.IsNullOrWhiteSpace(code) && _airlines.TryGetValue(code.Trim(), out var name) ? name : "";

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
        var index = _index;
        if (index == null || (!index.ContainsKey(title) && (now - _indexedUtc).TotalMinutes >= ReindexMinutes))
        {
            // A title no package declares is common (the simulator's own streamed traffic), so the packages
            // are read again only when their folders have changed since the last read.
            string? root = _packagesRoot();
            string stamp = root != null ? PackagesStamp(root) : "";
            if (index == null || stamp != _indexStamp)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                index = root != null ? BuildIndex(root) : new Dictionary<string, ModelRef>(StringComparer.OrdinalIgnoreCase);
                _airlines = AirlineNames(index.Values);
                _index = index;
                _indexStamp = stamp;
                _indexVersion++;
                _log.Info($"ev=model-index titles={index.Count} airlines={_airlines.Count} ms={clock.ElapsedMilliseconds}");
            }
            _indexedUtc = now;
        }
        if (title == IndexOnly) return;
        if (!index.TryGetValue(title, out var model))
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

    // What the packages folders hold, cheaply: every package folder's name and date, and the date of its
    // aircraft folder. A package installed, removed or updated changes it; no file inside one is read.
    internal static string PackagesStamp(string packagesRoot)
    {
        var all = new System.Text.StringBuilder();
        foreach (string folder in Database.MsfsPackageLayout.PackageFolders(packagesRoot))
            foreach (string package in SafeDirectories(folder))
            {
                all.Append(package).Append('|');
                try
                {
                    all.Append(Directory.GetLastWriteTimeUtc(package).Ticks);
                    // A traffic library's manager adds a livery as a new folder here, which leaves the
                    // package folder's own date alone.
                    string airplanes = Path.Combine(package, "SimObjects", "Airplanes");
                    if (Directory.Exists(airplanes)) all.Append('|').Append(Directory.GetLastWriteTimeUtc(airplanes).Ticks);
                }
                catch (Exception) { }
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
        string? baseDir = null, title = null, suffix = null, airlineCode = null, airline = null;
        string icao = "";
        var found = new List<(string Title, string Suffix, string AirlineCode, string Airline)>();
        void Close()
        {
            if (title != null) found.Add((title, suffix ?? "", (airlineCode ?? "").ToUpperInvariant(), airline ?? ""));
            title = suffix = airlineCode = airline = null;
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
            else if (section == "GENERAL" && key == "icao_type_designator")
                icao = value.ToUpperInvariant();
            else if (section.StartsWith("FLTSIM.", StringComparison.Ordinal))
            {
                if (key == "title") title = value;
                else if (key == "model") suffix = value;
                else if (key == "icao_airline") airlineCode = value;
                else if (key == "atc_airline") airline = value;
            }
        }
        Close();
        foreach (var (t, s, code, name) in found)
            if (t.Length > 0) index.TryAdd(t, new ModelRef(dir, s, baseDir, icao, code, name));   // the first package to name a title keeps it
    }

    /// <summary>
    /// Airline code → the name to say, learnt from the liveries that declare both an <c>icao_airline</c>
    /// and an <c>atc_airline</c>: the name most of that code's liveries carry (libraries disagree: one
    /// writes "SPEEDBIRD", another "Speedbird", a wet lease carries its operator's), in the spelling that
    /// is not all capitals when there is one, since a screen reader may spell a short capitalised word out.
    /// Only three-letter codes: AIG marks its private aircraft with made-up longer ones ("GA2J").
    /// </summary>
    internal static Dictionary<string, string> AirlineNames(IEnumerable<ModelRef> models)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in models)
        {
            if (m.AirlineCode.Length != 3 || m.Airline.Length == 0 || !m.AirlineCode.All(char.IsAsciiLetter)) continue;
            if (!counts.TryGetValue(m.AirlineCode, out var names)) counts[m.AirlineCode] = names = new Dictionary<string, int>(StringComparer.Ordinal);
            names[m.Airline] = names.GetValueOrDefault(m.Airline) + 1;
        }
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, names) in counts)
        {
            // The name, whatever its capitals, that most liveries carry; then its best spelling.
            var winner = names.GroupBy(n => n.Key, n => n, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Sum(n => n.Value)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).First();
            result[code] = winner.OrderBy(n => n.Key.Any(char.IsLower) ? 0 : 1).ThenByDescending(n => n.Value)
                .ThenBy(n => n.Key, StringComparer.Ordinal).First().Key;
        }
        return result;
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

    [GeneratedRegex(@"<LODS[^>]*\bTemplate\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LodsTemplateAttribute();

    [GeneratedRegex(@"<MergeModel>\s*([^<]+?)\s*</MergeModel>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MergeModelElement();

    /// <summary>
    /// The model's glTF files, most detailed first: the levels of detail its <c>model.cfg</c> names through
    /// its model XML, looked for in the aircraft's own model folder and then in its base container's.
    /// Empty when none is on disk.
    /// </summary>
    internal static IReadOnlyList<string> LodPaths(ModelRef model)
        => ClassicGroups(model).FirstOrDefault(g => g.Count > 0) ?? (IReadOnlyList<string>)Array.Empty<string>();

    // An ordinary aircraft's model: its levels of detail, then anything its model XML merges into them.
    private static List<List<string>> ClassicGroups(ModelRef model)
    {
        string folder = model.ModelSuffix.Length > 0 ? "model." + model.ModelSuffix : "model";
        foreach (string? root in new[] { model.AircraftDir, model.BaseDir })
        {
            if (root == null) continue;
            string modelDir;
            try { modelDir = Vfs(Path.GetFullPath(Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)))); }
            catch (Exception) { continue; }
            if (!Directory.Exists(modelDir)) continue;
            var groups = GroupsOf(modelDir);
            if (groups.Any(g => g.Count > 0)) return groups;
        }
        return new List<List<string>>();
    }

    /// <summary>
    /// The parts a title's model is made of: the one model for an ordinary aircraft (and whatever its model
    /// XML merges into it); for an MSFS 2024 MODULAR aircraft (its <c>aircraft.cfg</c> sits beside an
    /// <c>attached_objects.cfg</c>, in <c>presets/&lt;maker&gt;/&lt;preset&gt;/config</c>) the exterior it is
    /// assembled from: the common model, then every <c>[SIM_ATTACHMENT.N]</c> and <c>[MERGE_MODEL.N]</c> that
    /// goes to the exterior, and the attachments of those in turn. Empty when nothing is on disk.
    /// </summary>
    internal static IReadOnlyList<GroundTrafficModelReader.ModelPart> Parts(ModelRef model)
    {
        var parts = new List<GroundTrafficModelReader.ModelPart>();
        foreach (var group in ClassicGroups(model))
            if (group.Count > 0) parts.Add(new GroundTrafficModelReader.ModelPart(group));
        if (parts.Count > 0) return parts;

        string attached = Path.Combine(model.AircraftDir, "attached_objects.cfg");
        if (!File.Exists(attached)) return parts;
        int attachedFrom = 0;
        bool wholeAirframe = false;
        try
        {
            string? packageRoot = PackageRootOf(model.AircraftDir);
            if (packageRoot == null) return parts;
            // presets/<maker>/<preset>/config → the aircraft's own folder, four levels up: its common model.
            string aircraftRoot = Path.GetFullPath(Path.Combine(model.AircraftDir, "..", "..", "..", ".."));
            // The preset may name its model (model = "GE"): then common/model.GE is the airframe itself, whole,
            // and what is attached to it may carry no geometry at all (the TFDi MD-11's one attachment is its
            // navigation graph). Unnamed, common/model is the shared helper the airframe is attached to.
            string named = Vfs(Path.Combine(aircraftRoot, "common", "model." + model.ModelSuffix));
            wholeAirframe = model.ModelSuffix.Length > 0 && Directory.Exists(named);
            int parent = AddGroups(parts, GroupsOf(wholeAirframe ? named : Vfs(Path.Combine(aircraftRoot, "common", "model"))), -1, null);
            attachedFrom = parts.Count;
            AddAttachments(parts, attached, packageRoot, parent, depth: 0);
        }
        catch (Exception) { /* unreadable: whatever was gathered */ }
        // The shared common model alone is a helper with no airframe: with nothing attached there is nothing to measure.
        return parts.Count > attachedFrom || (wholeAirframe && parts.Count > 0) ? parts : new List<GroundTrafficModelReader.ModelPart>();
    }

    // Adds a model's groups as parts sharing one parent and attach node (what is merged into a model
    // shares its origin); returns the index of its main part, or the parent when it has no geometry.
    private static int AddGroups(List<GroundTrafficModelReader.ModelPart> parts, List<List<string>> groups, int parent, string? node)
    {
        int self = parent;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].Count == 0) continue;
            parts.Add(new GroundTrafficModelReader.ModelPart(groups[i], parent, node));
            if (i == 0) self = parts.Count - 1;
        }
        return self;
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
        string? root = null, file = null, toModel = null, toNode = null, mergeFile = null;
        void Close()
        {
            bool exterior = string.Equals(toModel, "exterior", StringComparison.OrdinalIgnoreCase);
            if (section.StartsWith("SIM_ATTACHMENT.", StringComparison.Ordinal) && root != null && file != null && exterior
                && !NotAirframeAttachment().IsMatch(Path.GetFileName(root.TrimEnd('/', '\\')))
                && !NotAirframeAttachment().IsMatch(Path.GetFileName(file.Replace('\\', '/'))))
            {
                string attachmentDir = Path.GetFullPath(Path.Combine(packageRoot, root.Replace('/', Path.DirectorySeparatorChar)));
                var groups = ModelGroups(Vfs(Path.GetFullPath(Path.Combine(attachmentDir, file.Replace('/', Path.DirectorySeparatorChar)))), attachmentDir, 0);
                // An attachment with no geometry of its own still carries the parts attached to it.
                int self = AddGroups(parts, groups, parent, toNode);
                string nested = Vfs(Path.Combine(attachmentDir, "config", "attached_objects.cfg"));
                if (File.Exists(nested)) AddAttachments(parts, nested, packageRoot, self, depth + 1);
            }
            else if (section.StartsWith("MERGE_MODEL.", StringComparison.Ordinal) && mergeFile != null && exterior
                     && !NotAirframeAttachment().IsMatch(mergeFile))
            {
                string merged = Vfs(Path.GetFullPath(Path.Combine(packageRoot, mergeFile.Replace('/', Path.DirectorySeparatorChar))));
                AddGroups(parts, ModelGroups(merged, Path.GetDirectoryName(merged) ?? packageRoot, 0), parent, null);
            }
            root = file = toModel = toNode = mergeFile = null;
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
                case "attach_to_model": case "merge_to_model": toModel = value; break;
                case "attach_to_node": toNode = value; break;
                case "merge_file": mergeFile = value; break;
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

    // A model folder's groups: what its model.cfg's exterior model names, else the glTF files in it.
    private static List<List<string>> GroupsOf(string modelDir)
    {
        var groups = new List<List<string>>();
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
                    groups = ModelGroups(
                        Vfs(Path.GetFullPath(Path.Combine(modelDir, Unquote(line[(eq + 1)..]).Replace('/', Path.DirectorySeparatorChar)))), modelDir, 0);
                    if (groups.Any(g => g.Count > 0)) break;
                }
            }
            if (!groups.Any(g => g.Count > 0) && Directory.Exists(modelDir))
                groups = new List<List<string>> { GltfsIn(modelDir).ToList() };
        }
        catch (Exception) { /* unreadable: no model */ }
        return groups;
    }

    /// <summary>
    /// What a model file is made of. The FIRST group is its own levels of detail, most detailed first:
    /// itself when it is a glTF; the files its model XML lists; those of the XML its
    /// <c>&lt;LODS Template="…"&gt;</c> names when it lists none of its own (the Fenix's sharklet wings are the
    /// plain wings' levels plus a merged model); else the glTF files beside the XML. Each further group is
    /// a model merged into them (<c>&lt;MergeModel&gt;</c>, one file per level): it shares the model's origin.
    /// </summary>
    internal static List<List<string>> ModelGroups(string file, string fallbackDir, int depth)
    {
        var groups = new List<List<string>> { new() };
        try
        {
            if (file.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(file)) groups[0].Add(file);
                return groups;
            }
            // An FSX-era model.cfg names its model with no extension: "normal = model" is model.mdl.
            if (!File.Exists(file))
            {
                if (File.Exists(file + ".mdl")) groups[0].Add(file + ".mdl");
                return groups;
            }
            string xmlDir = Path.GetDirectoryName(file) ?? fallbackDir;
            string xml = File.ReadAllText(file);
            string Resolve(string relative) => Vfs(Path.GetFullPath(Path.Combine(xmlDir, relative.Trim().Replace('/', Path.DirectorySeparatorChar))));

            foreach (Match m in ModelFileAttribute().Matches(xml))
            {
                string lod = Resolve(m.Groups[1].Value);
                if (File.Exists(lod) && !groups[0].Contains(lod, StringComparer.OrdinalIgnoreCase)) groups[0].Add(lod);
            }
            var merged = new List<string>();
            foreach (Match m in MergeModelElement().Matches(xml))
            {
                string lod = Resolve(m.Groups[1].Value);
                if (File.Exists(lod) && !merged.Contains(lod, StringComparer.OrdinalIgnoreCase)) merged.Add(lod);
            }
            if (groups[0].Count == 0 && depth < 4 && LodsTemplateAttribute().Match(xml) is { Success: true } template)
            {
                var inherited = ModelGroups(Resolve(template.Groups[1].Value), xmlDir, depth + 1);
                groups[0] = inherited[0];
                groups.AddRange(inherited.Skip(1));
            }
            if (merged.Count > 0) groups.Add(merged);
            if (groups.All(g => g.Count == 0)) groups[0].AddRange(GltfsIn(xmlDir));
        }
        catch (Exception) { /* unreadable: no model */ }
        return groups;
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
