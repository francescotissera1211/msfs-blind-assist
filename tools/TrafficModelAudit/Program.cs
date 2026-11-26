// Measures EVERY aircraft model installed on this PC with the app's own code
// (GroundTrafficModelCatalog + GroundTrafficModelReader) and holds each against the published size of
// the type its aircraft.cfg declares (GroundTrafficAircraftSizes). Run it after touching the reader, the
// size table or the type resolver, and after a traffic library changes its models:
//
//   dotnet run --project tools/TrafficModelAudit -c Debug -p:Platform=x64 [-- --all] [-- --package aig]
//
// One line per model that needs a look (--all prints every model), then a summary per package:
//   OK          measured, within tolerance of the published length and span
//   VARIANT     measured, but not the published size: winglets, a stretch, a mis-declared type, a rotor
//   NO-TYPE     measured, its declared type is not in the size table (the measurement is still used)
//   UNREADABLE  the model's geometry could not be measured: the app falls back to the published size
//   NO-FILE     the title's model file is not on disk (streamed or encrypted package)
// and how the title-only type resolver did on every installed title (what the app falls back on).
using System.Globalization;
using MSFSBlindAssist.Services;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
bool all = args.Contains("--all");
string? only = args.SkipWhile(a => a != "--package").Skip(1).FirstOrDefault();
const double LengthTolerance = 0.03, SpanTolerance = 0.05;

string? root = AircraftCfgCatalog.FindInstalledPackagesPath();
if (root == null) { Console.WriteLine("No MSFS packages folder found (UserCfg.opt)."); return 1; }
Console.WriteLine($"Packages: {root}");
var clock = System.Diagnostics.Stopwatch.StartNew();
var index = GroundTrafficModelCatalog.BuildIndex(root);
var icaoByTitle = AircraftCfgCatalog.BuildMap();
Console.WriteLine($"Index: {index.Count} titles in {clock.ElapsedMilliseconds} ms");

// One row per model FILE: liveries share it.
var models = new Dictionary<string, (List<string> Titles, IReadOnlyList<GroundTrafficModelReader.ModelPart> Parts, string Dir, string Suffix)>(StringComparer.OrdinalIgnoreCase);
var noFile = new List<string>();
foreach (var (title, model) in index)
{
    if (only != null && model.AircraftDir.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
    var parts = GroundTrafficModelCatalog.Parts(model);
    if (parts.Count == 0) { noFile.Add(title); continue; }
    string key = GroundTrafficModelCatalog.PartsKey(parts);
    if (!models.TryGetValue(key, out var row)) models[key] = row = (new List<string>(), parts, model.AircraftDir, model.ModelSuffix);
    row.Titles.Add(title);
}

string Package(string path)
{
    string rel = Path.GetRelativePath(root, path);
    var parts = rel.Split(Path.DirectorySeparatorChar);
    return parts.Length > 1 ? parts[1] : parts[0];
}

var summary = new SortedDictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
int[] Count(string package) => summary.TryGetValue(package, out var c) ? c : summary[package] = new int[5];
int measuredTitles = 0;
clock.Restart();
foreach (var (_, row) in models.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase))
{
    string file = row.Parts[^1].Lods[0];
    string package = Package(row.Dir);
    string label = row.Parts.Count > 1
        ? $"{Path.GetFileName(Path.GetDirectoryName(row.Dir))} ({row.Parts.Count} parts)"
        : $"{Path.GetFileName(row.Dir)}/{(row.Suffix.Length > 0 ? "model." + row.Suffix : "model")}";
    string declared = icaoByTitle.TryGetValue(row.Titles[0].ToLowerInvariant(), out var d) ? d : "";
    var published = GroundTrafficAircraftSizes.Lookup(declared);
    var big = new List<string>();
    if (args.Contains("--nodes"))
        GroundTrafficModelReader.NodeTrace = (dir, node, x0, x1, z0, z1) =>
        {
            double ahead = double.TryParse(Environment.GetEnvironmentVariable("AUDIT_AHEAD"), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.MaxValue;
            if (z1 > ahead || Math.Max(Math.Max(Math.Abs(x0), Math.Abs(x1)), Math.Max(Math.Abs(z0), Math.Abs(z1))) > 40 || x1 - x0 > 66)
                big.Add($"    node {Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar))}/{node} x[{x0:0},{x1:0}] z[{z0:0},{z1:0}]");
        };
    var outline = GroundTrafficModelReader.Measure(row.Parts, out string why);
    GroundTrafficModelReader.NodeTrace = null;
    foreach (string line in big.Distinct().Take(25)) Console.WriteLine(line);
    if (outline == null)
    {
        Count(package)[3]++;
        Console.WriteLine($"UNREADABLE {package} {label} type={declared} titles={row.Titles.Count} file={Path.GetFileName(file)} why={why}");
        if (args.Contains("--parts")) foreach (var part in row.Parts) Console.WriteLine($"    part parent={part.Parent} node={part.AttachNode} lods={part.Lods.Count} first={Path.GetFileName(part.Lods[0])}");
        continue;
    }
    measuredTitles += row.Titles.Count;
    if (args.Contains("--profile"))
        Console.WriteLine("    profile " + string.Join(" ", Enumerable.Range(0, outline.HalfWidths.Count).Where(i => i % 8 == 0)
            .Select(i => $"{-outline.TailM + i * AircraftOutline.StationM:0}:{outline.HalfWidths[i]:0.0}")));
    double length = outline.LengthM, span = outline.HalfSpanM * 2.0;
    string measured = $"L {length:0.00} span {span:0.00} nose {outline.NoseM:0.0} tail {outline.TailM:0.0} corners {outline.Polygon.Count}";
    if (published is not { } p)
    {
        Count(package)[2]++;
        Console.WriteLine($"NO-TYPE    {package} {label} type='{declared}' {measured} titles={row.Titles.Count}");
        continue;
    }
    double dl = (length - p.LengthM) / p.LengthM, dw = (span - p.WingspanM) / p.WingspanM;
    bool ok = Math.Abs(dl) <= LengthTolerance && Math.Abs(dw) <= SpanTolerance;
    Count(package)[ok ? 0 : 1]++;
    if (!ok || all)
        Console.WriteLine($"{(ok ? "OK        " : "VARIANT   ")} {package} {label} {declared}: {measured} | published L {p.LengthM:0.00} span {p.WingspanM:0.00} | dL {dl:+0%;-0%} dSpan {dw:+0%;-0%} titles={row.Titles.Count}");
}
foreach (string title in noFile) Count(Package(index[title].AircraftDir))[4]++;
foreach (var g in noFile.GroupBy(t => index[t].AircraftDir, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
    Console.WriteLine($"NO-FILE    {Package(g.Key)} {Path.GetFileName(g.Key)} model='{index[g.First()].ModelSuffix}' titles={g.Count()} e.g. \"{g.First()}\"");
Console.WriteLine();
Console.WriteLine($"Measured {models.Count} model files ({measuredTitles} titles) in {clock.ElapsedMilliseconds} ms");
Console.WriteLine("package: OK / VARIANT / NO-TYPE / UNREADABLE models, NO-FILE titles");
foreach (var (package, c) in summary) Console.WriteLine($"  {package}: {c[0]} / {c[1]} / {c[2]} / {c[3]}, {c[4]}");

// The fallback: the type read from the TITLE alone, against the type the package itself declares.
int same = 0, sameSize = 0, different = 0, unresolved = 0, undeclared = 0;
var wrong = new SortedDictionary<string, (string Example, int Count)>(StringComparer.OrdinalIgnoreCase);
foreach (var (title, model) in index)
{
    if (only != null && model.AircraftDir.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
    string declared = icaoByTitle.TryGetValue(title.ToLowerInvariant(), out var d) ? d : "";
    if (GroundTrafficAircraftSizes.Lookup(declared) is not { } want) { undeclared++; continue; }
    string got = GroundTrafficAircraftSizes.ResolveIcao(null, null, title);
    if (got.Length == 0) { unresolved++; Note($"{declared} -> (none)", title); continue; }
    if (string.Equals(got, declared, StringComparison.OrdinalIgnoreCase)) { same++; continue; }
    var have = GroundTrafficAircraftSizes.Lookup(got)!.Value;
    if (Math.Abs(have.LengthM - want.LengthM) <= 1.0 && Math.Abs(have.WingspanM - want.WingspanM) <= 2.0) { sameSize++; continue; }
    different++;
    Note($"{declared} -> {got}", title);
}
void Note(string key, string title) => wrong[key] = wrong.TryGetValue(key, out var w) ? (w.Example, w.Count + 1) : (title, 1);
Console.WriteLine();
Console.WriteLine($"Type from the title alone: {same} exact, {sameSize} same size, {different} different size, {unresolved} not recognised ({undeclared} titles declare a type the table lacks)");
foreach (var (key, w) in wrong) Console.WriteLine($"  {key} x{w.Count}: e.g. \"{w.Example}\"");
return 0;
