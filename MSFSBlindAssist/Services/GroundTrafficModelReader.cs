using System.Text.Json;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One aircraft model seen from above: half its width at every <see cref="StationM"/> along the fuselage,
/// from the tail to the nose, measured from the point the simulator reports as the aircraft's position
/// (the model's own origin). <see cref="Polygon"/> is the same silhouette as a closed outline, thinned to
/// the corners that matter.
/// </summary>
public sealed class AircraftOutline
{
    /// <summary>The spacing of the half-width stations, metres.</summary>
    public const double StationM = 0.25;

    /// <summary>The thinning tolerance of <see cref="Polygon"/>, metres: no part of the silhouette is cut by more.</summary>
    public const double SimplifyToleranceM = 0.25;

    /// <summary>Metres from the model origin back to the rearmost point.</summary>
    public double TailM { get; }
    /// <summary>Metres from the model origin forward to the foremost point.</summary>
    public double NoseM { get; }
    /// <summary>Half-width per station, tail first; station i is centred (i + 0.5) stations ahead of the tail.</summary>
    public IReadOnlyList<float> HalfWidths { get; }
    /// <summary>The widest half-width, metres.</summary>
    public double HalfSpanM { get; }
    /// <summary>The closed outline, as (X right, Z ahead) metres from the origin.</summary>
    public IReadOnlyList<(double X, double Z)> Polygon { get; }

    public double LengthM => NoseM + TailM;

    public AircraftOutline(double tailM, double noseM, IReadOnlyList<float> halfWidths)
    {
        TailM = tailM;
        NoseM = noseM;
        HalfWidths = halfWidths;
        HalfSpanM = halfWidths.Count == 0 ? 0.0 : halfWidths.Max();
        Polygon = BuildPolygon(tailM, noseM, halfWidths);
    }

    private static List<(double X, double Z)> BuildPolygon(double tailM, double noseM, IReadOnlyList<float> w)
    {
        // The right-hand side from the tail tip to the nose tip, then thinned.
        var right = new List<(double X, double Z)> { (0.0, -tailM) };
        for (int i = 0; i < w.Count; i++)
        {
            double z = Math.Clamp(-tailM + (i + 0.5) * StationM, -tailM, noseM);
            right.Add((Math.Max(0.0, w[i]), z));
        }
        right.Add((0.0, noseM));
        var keep = new bool[right.Count];
        keep[0] = keep[^1] = true;
        Simplify(right, 0, right.Count - 1, keep);
        var thinned = new List<(double X, double Z)>();
        for (int i = 0; i < right.Count; i++) if (keep[i]) thinned.Add(right[i]);

        var polygon = new List<(double X, double Z)>(thinned.Count * 2);
        polygon.AddRange(thinned);
        for (int i = thinned.Count - 2; i >= 1; i--)
            if (thinned[i].X > 0.0) polygon.Add((-thinned[i].X, thinned[i].Z));
        return polygon;
    }

    // Douglas-Peucker: keeps the point farthest from each chord while it is beyond the tolerance.
    private static void Simplify(List<(double X, double Z)> p, int first, int last, bool[] keep)
    {
        if (last <= first + 1) return;
        var (ax, az) = p[first];
        var (bx, bz) = p[last];
        double dx = bx - ax, dz = bz - az, len2 = dx * dx + dz * dz;
        int farthest = -1;
        double worst = SimplifyToleranceM;
        for (int i = first + 1; i < last; i++)
        {
            double t = len2 < 1e-12 ? 0.0 : Math.Clamp(((p[i].X - ax) * dx + (p[i].Z - az) * dz) / len2, 0.0, 1.0);
            double cx = ax + dx * t, cz = az + dz * t;
            double d = Math.Sqrt((p[i].X - cx) * (p[i].X - cx) + (p[i].Z - cz) * (p[i].Z - cz));
            if (d > worst) { worst = d; farthest = i; }
        }
        if (farthest < 0) return;
        keep[farthest] = true;
        Simplify(p, first, farthest, keep);
        Simplify(p, farthest, last, keep);
    }
}

/// <summary>
/// Measures an installed aircraft model's outline from its own glTF geometry, so the ground-traffic
/// monitor's distances are to the model the pilot would see: its real nose, tail and wingtips, whatever
/// the brand, the variant or the winglets. Every traffic library installed on 2026-11-26 (AIG, FSLTL,
/// IVAO) ships plain glTF 2.0 with a <c>.bin</c> of float positions.
/// <para>What the measurement rests on, each checked against those libraries with tools/TrafficModelAudit
/// (1,010 model files; lengths within 1% of the published figure wherever the model is the type it says
/// it is). The full account is [TRF-12]:</para>
/// <list type="bullet">
/// <item>The model origin is the point <c>PLANE LATITUDE/LONGITUDE</c> reports, +Z is the nose and X is
/// lateral. <c>reference_datum_position</c> moves the flight model's datum, never the origin.</item>
/// <item>A SKINNED mesh (a flexing wing, a slat, a flap, a tailplane) stores its vertices in the frame its
/// SKELETON stands in, the parent of the node its skin names as <c>skeleton</c>: it is placed there
/// (<c>AddSkinned</c>). Its own node's transform is not applied, as glTF itself says, and neither are the
/// skin's inverse bind matrices: in these exports they only undo that node transform, and with the joints
/// as the file poses them they put the BlueBird 757's fin at its nose. Read as stored, which is right only
/// where the skeleton stands at the origin, the PMDG 737's wing lay along its fuselage and the PMDG 777's
/// tailplane sat at its wing. The first level of detail with no skinned mesh is still preferred: it
/// needs no placing.</item>
/// <item>A node that an animation moves but that declares no transform of its own takes the animation's
/// first key (flaps and props retracted or at rest); a node that does declare one keeps it.</item>
/// <item>Landing gear never shapes an aircraft's outline from above, and its rest pose is the least
/// trustworthy part of a model (FSLTL's 777 nose gear rests 28 m below it and 6 m ahead of the nose):
/// a node a gear, wheel or steering animation moves is left out, with everything under it
/// (<see cref="GearAnimation"/>).</item>
/// <item>Ground equipment drawn inside an aircraft model (IVAO's pushback tug, fuel truck and stairs,
/// wheel chocks) is not airframe: <see cref="NotAirframe"/> names it. It is drawn with ordinary materials
/// and shown or hidden by the simulator, so only its name tells it apart.</item>
/// <item>What is drawn with the simulator's INVISIBLE material (<c>ASOBO_material_invisible</c>) or as an
/// environment occluder is never seen, and what is drawn see-through as a decal or with no colour at all is
/// light or a picture on the surface (<c>NotSolid</c>), so neither is ever airframe: the 31 m sphere the PMDG 777 hangs on
/// each light so the lamp is not culled, the 35 m boxes on the iniBuilds A350's wings, the Fenix's 200 m
/// triangles and the box round its fuselage, collision shapes, FSLTL's gear helpers. Over the 1,021
/// models installed on 2026-11-26 this one rule does what names once tried to ("LOD friend", "cast
/// shadow", a skeleton's box, anything called a light), and a NAME is no test for those: an
/// FSX-converted model keeps its whole airframe under a visibility condition called
/// <c>cabin_lights_VC</c> (353 of AIG's models, the business jets entirely), and AIG's 787 keeps its
/// wingtips under <c>LIGHT_</c> nodes.</item>
/// <item>The silhouette is laterally SYMMETRIC: each station takes the narrower of its two sides, which
/// drops a part parked on one side only (a catering truck, a hose), and only the one connected body is
/// kept. A rotor is the exception both ways: turning, it is drawn as a see-through square card and is
/// measured as the disc inside it (<c>Disc</c>); standing still with no card, its blades reach out on
/// both sides but never at one station, and the model takes the WIDER side (<see cref="StoppedRotorShare"/>).</item>
/// </list>
/// Pure but for reading the files; never throws: a model it cannot measure is null.
/// </summary>
public static partial class GroundTrafficModelReader
{
    /// <summary>
    /// A node whose name matches, and everything under it, is ground equipment, never airframe; so is a
    /// trailing drogue (the PMDG 737's test cone and its line reach 0.25 m behind the tailplane).
    /// </summary>
    [GeneratedRegex(@"ground.?serv|^aso_|pushback|fuel.?truck|(^|_)tug|(^|_)gpu|(^|_)gse(_|$)|chock|vis_blocks|stairs|catering|baggage|tow.?bar|^ramp_|marshal|drogue",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotAirframe();

    /// <summary>An animation whose name matches moves landing gear: its nodes are left out of the outline.</summary>
    [GeneratedRegex(@"gear|wheel|tire|tyre|bogie|steer", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GearAnimation();

    /// <summary>
    /// The audit's eyes (tools/TrafficModelAudit --nodes): called for every mesh node that goes into an
    /// outline, with its file, its name and its extent in model space. Null in the app.
    /// </summary>
    [ThreadStatic] internal static Action<string, string, double, double, double, double>? NodeTrace;

    /// <summary>
    /// Told, while a model is assembled and measured, of everything left out or placed by a fallback: an
    /// attach node that is nowhere, a scene hung on a node that is nowhere, a part none of whose levels
    /// can be read, an attachment with no model on disk or left out by its name. Such a part usually ends
    /// up INSIDE the outline, where the published length and span cannot show it (the PMDG 737's whole
    /// tail unit sat in its wing), or what the outline's own rules threw away: one side wider than the other,
    /// geometry apart from the body, a rotor standing still. Null in the app; <c>tools/TrafficModelAudit</c>
    /// lists what it is told. Per thread, so one test's listener never hears another's model.
    /// </summary>
    [ThreadStatic] internal static Action<string>? Notes;

    /// <summary>A mesh of at most this many vertices spread more than <see cref="HelperMinSpreadM"/> both across and along is a helper plane, not airframe.</summary>
    public const int HelperMaxVertices = 36;
    public const double HelperMinSpreadM = 40.0;

    /// <summary>
    /// A model whose symmetric outline is narrower than this share of what EACH of its sides reaches somewhere
    /// along it is not the same on both sides station by station: a rotor standing still. See <c>Silhouette</c>.
    /// </summary>
    public const double StoppedRotorShare = 0.75;

    /// <summary>The corners of the many-sided figure a blurred rotor or propeller disc is measured as.</summary>
    public const int DiscCorners = 48;

    /// <summary>Stations this far apart (metres) or less belong to one body; a part beyond it is dropped.</summary>
    public const double MaxBodyGapM = 2.0;

    /// <summary>A measured outline outside these bounds (metres) is not an aircraft: the model is not used.</summary>
    public const double MinLengthM = 2.0, MaxLengthM = 110.0, MinHalfSpanM = 0.5, MaxHalfSpanM = 50.0;

    /// <summary>
    /// One part of a model: its levels of detail, most detailed first, and, for a part attached to
    /// another (an MSFS 2024 modular aircraft: a fuselage, a wing, a stabiliser), the index of that part and
    /// the name of the node of it that this one is placed at (none: its origin).
    /// </summary>
    public sealed record ModelPart(IReadOnlyList<string> Lods, int Parent = -1, string? AttachNode = null);

    /// <summary>
    /// A level of detail whose geometry is larger than this (bytes) is passed over for a lighter one when
    /// there is one: an add-on's top level can be hundreds of megabytes, and the outline is the same.
    /// </summary>
    public const long PreferredMaxBufferBytes = 48L * 1024 * 1024;

    /// <summary>A level of detail larger than this (bytes) is the last resort.</summary>
    public const long MaxBufferBytes = 256L * 1024 * 1024;

    /// <summary>The number of levels of detail, from the most detailed, that are fit to measure: the coarser ones drop or merge parts.</summary>
    public const int DetailedLods = 3;

    private static readonly double[] Identity = { 1.0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };

    /// <summary>
    /// The outline of a model given its levels of detail, most detailed first: measured from the first
    /// one with no skinned mesh, else from the first. Null when none can be read.
    /// </summary>
    public static AircraftOutline? Measure(IReadOnlyList<string> lodPaths) => Measure(new[] { new ModelPart(lodPaths) });

    /// <summary>The outline of one glTF file; null when it cannot be read or is not aircraft-sized.</summary>
    public static AircraftOutline? Measure(string gltfPath) => Measure(new[] { gltfPath });

    /// <summary>
    /// The outline of a model assembled from <paramref name="parts"/>, parents before the parts attached to
    /// them. A part that cannot be read is left out; null when nothing can be measured.
    /// </summary>
    public static AircraftOutline? Measure(IReadOnlyList<ModelPart> parts) => Measure(parts, out _);

    /// <summary>As <see cref="Measure(IReadOnlyList{ModelPart})"/>, saying in <paramref name="why"/> why a model could not be measured (the audit and the log).</summary>
    public static AircraftOutline? Measure(IReadOnlyList<ModelPart> parts, out string why)
    {
        why = "";
        try
        {
            var meshes = new List<Part>();
            // Every node placed so far, by name and by unique id, whatever part it came in: where a scene
            // that names a node of another part goes (the Fenix's wingtip devices name the wing's tip).
            var placed = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            // A node may be named before the part that holds it has been read (the PMDG 737's fuselage
            // hangs a scene on a node of the ROOT.gltf that follows it). The model is then assembled a
            // second time, with every node the first time placed to look such a name up in.
            Dictionary<string, double[]>? later = null;
            var listener = Notes;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    meshes.Clear();
                    placed.Clear();
                    var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var said = new List<string>();
                    Notes = listener == null ? null : said.Add;
                    Assemble(parts, meshes, placed, later, missing);
                    if (pass == 0 && missing.Any(placed.ContainsKey))
                    {
                        later = new Dictionary<string, double[]>(placed, StringComparer.OrdinalIgnoreCase);
                        continue;
                    }
                    foreach (string note in said) listener?.Invoke(note);
                    break;
                }
            }
            finally { Notes = listener; }
            if (meshes.Count == 0) { why = "no geometry could be read"; return null; }
            return Silhouette(meshes, out why);
        }
        catch (Exception ex) { why = ex.Message; return null; }
    }

    // Reads every part in order, each at its parent's attach node. A name found nowhere goes into `missing`.
    private static void Assemble(IReadOnlyList<ModelPart> parts, List<Part> meshes, Dictionary<string, double[]> placed,
        IReadOnlyDictionary<string, double[]>? later, HashSet<string> missing)
    {
        var roots = new double[parts.Count][];
        var nodes = new Dictionary<string, double[]>?[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            double[] root = Identity;
            if (part.Parent >= 0 && part.Parent < i)
            {
                root = roots[part.Parent];
                // The node it goes on is the parent's own, or one of a model merged into the parent and
                // read as a part of its own: the PMDG 737 keeps every attach point (tail, gear, cockpit)
                // in a ROOT.gltf beside its fuselage. Looked for in the fuselage alone, the tail unit's
                // was not found, and fin, tailplane and elevators were all drawn at the aircraft's
                // origin, inside the wing's outline: every 737 measured 1.43 m short and with no
                // tailplane at all.
                if (part.AttachNode is { Length: > 0 } name)
                {
                    if (nodes[part.Parent] is { } known && known.TryGetValue(name, out var at)) root = at;
                    else if (placed.TryGetValue(name, out var elsewhere)) root = elsewhere;
                    else if (later != null && later.TryGetValue(name, out var afterwards)) root = afterwards;
                    else
                    {
                        missing.Add(name);
                        Notes?.Invoke($"attach node '{name}' is nowhere: {Path.GetFileName(part.Lods[0])} sits at its parent's origin");
                    }
                }
            }
            roots[i] = root;
            int had = meshes.Count;
            nodes[i] = Load(part.Lods, root, placed, later, missing, meshes);
            if (nodes[i] == null && meshes.Count == had)
                Notes?.Invoke($"no level of detail could be read: {Path.GetFileName(part.Lods[0])}");
            if (nodes[i] is { } own)
                foreach (var (id, world) in own) placed.TryAdd(id, world);
        }
    }

    // Adds one part's airframe meshes, from the level of detail that suits best. Only the first
    // DetailedLods levels are fit to measure (the PMDG 777's sixth has no tail cone): among them one with
    // no skinned mesh and not oversized first, then not oversized, then the rest in order; the coarse
    // levels only when no detailed one can be read. Returns the world transform of every node of the
    // level used, by name and by unique id (where the parts attached to it go).
    private static Dictionary<string, double[]>? Load(IReadOnlyList<string> lods, double[] root,
        IReadOnlyDictionary<string, double[]> placed, IReadOnlyDictionary<string, double[]>? later, HashSet<string> missing,
        List<Part> meshes)
    {
        // An FSX-era model (IVAO's 737-200, -400 and -500) is one binary file, not glTF.
        foreach (string lod in lods)
        {
            if (!lod.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)) continue;
            var legacy = ReadLegacyModel(lod, root);
            if (legacy.Count == 0) continue;
            meshes.AddRange(legacy);
            return new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        }

        var candidates = new List<(int Rank, int Order, JsonDocument Doc, string Path)>();
        try
        {
            for (int i = 0; i < lods.Count; i++)
            {
                JsonDocument doc;
                try { doc = JsonDocument.Parse(WithoutBom(File.ReadAllBytes(lods[i]))); }
                catch (Exception) { continue; }
                var r = doc.RootElement;
                bool skinned = r.TryGetProperty("skins", out var skins)
                               && skins.ValueKind == JsonValueKind.Array && skins.GetArrayLength() > 0;
                long bytes = 0;
                if (r.TryGetProperty("buffers", out var buffers) && buffers.ValueKind == JsonValueKind.Array)
                    foreach (var b in buffers.EnumerateArray())
                        if (b.TryGetProperty("byteLength", out var len) && len.TryGetInt64(out long n)) bytes += n;
                int rank = (i >= DetailedLods ? 16 : 0) + (bytes > MaxBufferBytes ? 8 : 0)
                           + (bytes > PreferredMaxBufferBytes ? 4 : 0) + (skinned ? 2 : 0);
                candidates.Add((rank, i, doc, lods[i]));
            }
            // A part with no airframe of its own (a modular aircraft's common model) still says where the
            // parts attached to it go: its nodes are kept from the best level that could be read.
            // What a level says it could not place counts only for the level that is used.
            Dictionary<string, double[]>? nodesOnly = null;
            List<string>? nodesOnlyNotes = null;
            HashSet<string>? nodesOnlyMissing = null;
            var listener = Notes;
            try
            {
                foreach (var c in candidates.OrderBy(c => c.Rank).ThenBy(c => c.Order))
                {
                    var said = new List<string>();
                    var unplaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    Notes = listener == null ? null : note => said.Add($"{note} ({Path.GetFileName(c.Path)})");
                    try
                    {
                        var gltf = new Gltf(c.Doc.RootElement, Path.GetDirectoryName(c.Path) ?? "");
                        var found = gltf.WorldParts(NotAirframe(), GearAnimation(), root, placed, later, unplaced, out var nodeWorlds);
                        if (found.Count == 0)
                        {
                            if (nodesOnly == null) { nodesOnly = nodeWorlds; nodesOnlyNotes = said; nodesOnlyMissing = unplaced; }
                            continue;
                        }
                        meshes.AddRange(found);
                        missing.UnionWith(unplaced);
                        foreach (string note in said) listener?.Invoke(note);
                        return nodeWorlds;
                    }
                    catch (Exception) { /* this level cannot be read: the next */ }
                }
                if (nodesOnlyMissing != null) missing.UnionWith(nodesOnlyMissing);
                if (nodesOnlyNotes != null) foreach (string note in nodesOnlyNotes) listener?.Invoke(note);
                return nodesOnly;
            }
            finally { Notes = listener; }
        }
        finally { foreach (var c in candidates) c.Doc.Dispose(); }
    }

    /// <summary>A legacy model larger than this (bytes) is not read.</summary>
    public const long MaxLegacyModelBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The geometry of an FSX-format <c>.MDL</c> (RIFF "MDLX"): the vertices of its <c>VERT</c> chunk, 32
    /// bytes each with the position first, in metres, X to the right and Z ahead of the same origin a glTF
    /// model has, and the triangle lists of its most detailed level (<c>INDE</c> indices, through the
    /// <c>PART</c> records of the <c>LODE</c> with the largest level). Checked on IVAO's 737-400: 28.88 m by 36.57 m as stored,
    /// against a published 28.88 by 36.45. A part an animation moves is stored about its own pivot, so
    /// it lands near the origin, inside the airframe, and changes nothing seen from above.
    /// </summary>
    internal static List<Part> ReadLegacyModel(string path, double[] root)
    {
        var parts = new List<Part>();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxLegacyModelBytes || info.Length < 16) return parts;
            byte[] b = File.ReadAllBytes(path);
            if (b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'M' || b[9] != 'D' || b[10] != 'L') return parts;

            int vertAt = -1, vertSize = 0, indeAt = -1, indeSize = 0;
            // Each LODE opens with its level's size on screen: the largest is the most detailed (the
            // smallest, listed first, is three vertices).
            var partRecords = new List<int>();
            List<int>? current = null;
            int bestLevel = int.MinValue;
            void Walk(int from, int to, int depth)
            {
                for (int at = from; at + 8 <= to;)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, at, 4);
                    int size = BitConverter.ToInt32(b, at + 4);
                    int data = at + 8;
                    if (size < 0 || (long)data + size > to) return;
                    switch (id)
                    {
                        case "MDLD": case "VERB": case "LODT": Walk(data, data + size, depth + 1); break;
                        case "LODE":
                            if (size >= 4)
                            {
                                int level = BitConverter.ToInt32(b, data);
                                current = new List<int>();
                                Walk(data + 4, data + size, depth + 1);
                                if (current.Count > 0 && level > bestLevel) { bestLevel = level; partRecords = current; }
                                current = null;
                            }
                            break;
                        case "VERT": if (vertAt < 0) { vertAt = data; vertSize = size; } break;
                        case "INDE": if (indeAt < 0) { indeAt = data; indeSize = size; } break;
                        case "PART": if (current != null && size >= 36) current.Add(data); break;
                    }
                    at = data + size + (size & 1);
                }
            }
            Walk(12, b.Length, 0);
            if (vertAt < 0) return parts;

            int count = vertSize / 32;
            var xz = new double[count * 2];
            for (int i = 0; i < count; i++)
            {
                double x = BitConverter.ToSingle(b, vertAt + i * 32), y = BitConverter.ToSingle(b, vertAt + i * 32 + 4),
                       z = BitConverter.ToSingle(b, vertAt + i * 32 + 8);
                xz[i * 2] = root[0] * x + root[1] * y + root[2] * z + root[3];
                xz[i * 2 + 1] = root[8] * x + root[9] * y + root[10] * z + root[11];
            }
            // The triangle lists of the first level: indices are relative to each part's first vertex.
            var triangles = new List<int>();
            int indices = indeSize / 2;
            foreach (int at in partRecords)
            {
                int type = BitConverter.ToInt32(b, at), vertexOffset = BitConverter.ToInt32(b, at + 16),
                    indexOffset = BitConverter.ToInt32(b, at + 24), indexCount = BitConverter.ToInt32(b, at + 28);
                if (type != 1 || indeAt < 0 || indexOffset < 0 || indexCount <= 0 || indexOffset + indexCount > indices) continue;
                for (int i = 0; i + 2 < indexCount; i += 3)
                {
                    int p = vertexOffset + BitConverter.ToUInt16(b, indeAt + (indexOffset + i) * 2),
                        q = vertexOffset + BitConverter.ToUInt16(b, indeAt + (indexOffset + i + 1) * 2),
                        r = vertexOffset + BitConverter.ToUInt16(b, indeAt + (indexOffset + i + 2) * 2);
                    if ((uint)p < (uint)count && (uint)q < (uint)count && (uint)r < (uint)count) { triangles.Add(p); triangles.Add(q); triangles.Add(r); }
                }
            }
            parts.Add(new Part(xz, triangles.Count > 0 ? triangles.ToArray() : null));
        }
        catch (Exception) { parts.Clear(); }
        return parts;
    }

    private static ReadOnlyMemory<byte> WithoutBom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsMemory(3) : bytes;

    /// <summary>One mesh primitive in model space: its points as X, Z pairs and its triangles' corner indices.</summary>
    internal readonly record struct Part(double[] Xz, int[]? Triangles);

    internal static AircraftOutline? Silhouette(IReadOnlyList<Part> parts) => Silhouette(parts, out _);

    internal static AircraftOutline? Silhouette(IReadOnlyList<Part> parts, out string why)
    {
        why = "";
        double zMin = double.MaxValue, zMax = double.MinValue;
        foreach (var part in parts)
            for (int i = 1; i < part.Xz.Length; i += 2)
            {
                double z = part.Xz[i];
                if (!double.IsFinite(z) || !double.IsFinite(part.Xz[i - 1])) { why = "a position is not a number"; return null; }
                if (z < zMin) zMin = z;
                if (z > zMax) zMax = z;
            }
        if (zMin > zMax || zMax - zMin > 4.0 * MaxLengthM)
        {
            why = FormattableString.Invariant($"geometry spans {zMax - zMin:0} m along the fuselage");
            return null;
        }

        int n = (int)Math.Floor((zMax - zMin) / AircraftOutline.StationM) + 1;
        var cells = new Cells(n, zMin);
        foreach (var part in parts)
        {
            var xz = part.Xz;
            if (part.Triangles is { } tri)
            {
                for (int t = 0; t + 2 < tri.Length; t += 3)
                {
                    int a = tri[t], b = tri[t + 1], c = tri[t + 2];
                    if ((uint)a * 2 + 1 >= (uint)xz.Length || (uint)b * 2 + 1 >= (uint)xz.Length || (uint)c * 2 + 1 >= (uint)xz.Length) continue;
                    cells.AddEdge(xz[a * 2], xz[a * 2 + 1], xz[b * 2], xz[b * 2 + 1]);
                    cells.AddEdge(xz[b * 2], xz[b * 2 + 1], xz[c * 2], xz[c * 2 + 1]);
                    cells.AddEdge(xz[c * 2], xz[c * 2 + 1], xz[a * 2], xz[a * 2 + 1]);
                }
            }
            else
            {
                for (int i = 0; i + 1 < xz.Length; i += 2) cells.AddPoint(xz[i], xz[i + 1]);
            }
        }

        // The narrower side at each station; a station with geometry on one side only is empty.
        var width = new float[n];
        float reachRight = 0f, reachLeft = 0f;
        for (int i = 0; i < n; i++)
        {
            width[i] = cells.Right[i] >= 0f && cells.Left[i] >= 0f ? Math.Min(cells.Right[i], cells.Left[i]) : -1f;
            reachRight = Math.Max(reachRight, cells.Right[i]);
            reachLeft = Math.Max(reachLeft, cells.Left[i]);
        }
        var (bestStart, bestEnd) = Body(width);

        // Both sides reach far out, but never at the same station: a rotor standing still, its blades
        // wherever they stopped. The narrower side would cut every blade off, so such a model takes the
        // WIDER side, the blades and their mirror image, which is where they are once the rotor turns.
        float widest = 0f;
        for (int i = Math.Max(0, bestStart); i <= bestEnd; i++) widest = Math.Max(widest, width[i]);
        bool stoppedRotor = reachRight > 0f && reachLeft > 0f && widest < StoppedRotorShare * Math.Min(reachRight, reachLeft);
        if (stoppedRotor)
        {
            for (int i = 0; i < n; i++) width[i] = Math.Max(cells.Right[i], cells.Left[i]);
            (bestStart, bestEnd) = Body(width);
        }
        if (bestStart < 0) { why = "no station has geometry on both sides"; return null; }

        if (Notes is { } notes)
        {
            // What the two rules above threw away, for the audit: an aircraft is the same on both sides and
            // in one piece, so a lot of either is a part in the wrong place (or ground equipment).
            if (stoppedRotor)
                notes(FormattableString.Invariant($"the two sides reach {reachRight:0.0} and {reachLeft:0.0} m, never at one station (a rotor standing still): the wider side is taken"));
            int lopsided = 0, apart = 0, first = -1, last = -1, rightWider = 0;
            float most = 0f;
            for (int i = 0; i < n; i++)
            {
                float r = Math.Max(0f, cells.Right[i]), l = Math.Max(0f, cells.Left[i]);
                if (i < bestStart || i > bestEnd) { if (r > 0.5f || l > 0.5f) apart++; }
                else if (Math.Abs(r - l) > 1f)
                {
                    lopsided++;
                    most = Math.Max(most, Math.Abs(r - l));
                    if (first < 0) first = i;
                    last = i;
                    if (r > l) rightWider++;
                }
            }
            if (!stoppedRotor && lopsided * AircraftOutline.StationM >= 2.0)
                notes(FormattableString.Invariant($"one side is wider than the other by up to {most:0.0} m over {lopsided * AircraftOutline.StationM:0.0} m of the length, z {zMin + first * AircraftOutline.StationM:0.0} to {zMin + (last + 1) * AircraftOutline.StationM:0.0}, +x wider at {rightWider} of {lopsided} stations (the narrower side is taken)"));
            if (apart * AircraftOutline.StationM >= 1.0)
                notes(FormattableString.Invariant($"{apart * AircraftOutline.StationM:0.0} m of geometry apart from the body is left out"));
        }

        var half = new float[bestEnd - bestStart + 1];
        for (int i = bestStart; i <= bestEnd; i++) half[i - bestStart] = Math.Max(0f, width[i]);
        double tailZ = cells.ZLow[bestStart], noseZ = cells.ZHigh[bestEnd];
        // The stations are laid out from the tail tip, so re-anchor them on it.
        double tailM = -tailZ, noseM = noseZ;
        var outline = new AircraftOutline(tailM, noseM, Resample(half, zMin + bestStart * AircraftOutline.StationM, tailZ, noseZ));
        if (outline.LengthM < MinLengthM || outline.LengthM > MaxLengthM
            || outline.HalfSpanM < MinHalfSpanM || outline.HalfSpanM > MaxHalfSpanM)
        {
            why = FormattableString.Invariant($"not aircraft-sized: {outline.LengthM:0.0} m long, {outline.HalfSpanM * 2:0.0} m wide");
            return null;
        }
        return outline;
    }

    // The one connected body: the run of stations with the most of them occupied (width 0 or more), a gap
    // of up to MaxBodyGapM bridged. (-1, -1) when no station is occupied.
    private static (int Start, int End) Body(float[] width)
    {
        int n = width.Length;
        int maxGap = (int)Math.Round(MaxBodyGapM / AircraftOutline.StationM);
        int bestStart = -1, bestEnd = -1, bestCount = 0;
        int start = -1, end = -1, count = 0, gap = 0;
        for (int i = 0; i <= n; i++)
        {
            bool occupied = i < n && width[i] >= 0f;
            if (occupied)
            {
                if (start < 0) { start = i; count = 0; }
                end = i;
                count++;
                gap = 0;
            }
            else if (start >= 0 && (++gap > maxGap || i == n))
            {
                if (count > bestCount) { bestCount = count; bestStart = start; bestEnd = end; }
                start = -1;
            }
        }
        return (bestStart, bestEnd);
    }

    // The cells start at a multiple of the station from zMin, the outline's stations at its tail tip: a
    // station takes the widest cell it overlaps, so nothing is cut.
    private static float[] Resample(float[] half, double firstCellZ, double tailZ, double noseZ)
    {
        int n = Math.Max(1, (int)Math.Ceiling((noseZ - tailZ) / AircraftOutline.StationM - 1e-9));
        var result = new float[n];
        for (int i = 0; i < n; i++)
        {
            double from = tailZ + i * AircraftOutline.StationM, to = Math.Min(noseZ, from + AircraftOutline.StationM);
            int lo = Math.Clamp((int)Math.Floor((from - firstCellZ) / AircraftOutline.StationM + 1e-9), 0, half.Length - 1);
            int hi = Math.Clamp((int)Math.Floor((to - firstCellZ) / AircraftOutline.StationM - 1e-9), lo, half.Length - 1);
            float widest = 0f;
            for (int c = lo; c <= hi; c++) widest = Math.Max(widest, half[c]);
            result[i] = widest;
        }
        return result;
    }

    private sealed class Cells
    {
        public readonly float[] Right, Left, ZLow, ZHigh;
        private readonly double _zMin;
        private readonly int _n;

        public Cells(int n, double zMin)
        {
            _n = n;
            _zMin = zMin;
            Right = new float[n];
            Left = new float[n];
            ZLow = new float[n];
            ZHigh = new float[n];
            Array.Fill(Right, -1f);
            Array.Fill(Left, -1f);
            Array.Fill(ZLow, float.MaxValue);
            Array.Fill(ZHigh, float.MinValue);
        }

        private int Index(double z) => Math.Clamp((int)Math.Floor((z - _zMin) / AircraftOutline.StationM), 0, _n - 1);

        public void AddPoint(double x, double z) => Put(Index(z), x, z);

        private void Put(int i, double x, double z)
        {
            if (x >= 0.0 && x > Right[i]) Right[i] = (float)x;
            if (x <= 0.0 && -x > Left[i]) Left[i] = (float)-x;
            if (z < ZLow[i]) ZLow[i] = (float)z;
            if (z > ZHigh[i]) ZHigh[i] = (float)z;
        }

        // An edge crosses every station between its ends: each takes the edge where it enters and leaves it.
        public void AddEdge(double x1, double z1, double x2, double z2)
        {
            if (z2 < z1) { (x1, x2) = (x2, x1); (z1, z2) = (z2, z1); }
            int first = Index(z1), last = Index(z2);
            if (first == last) { Put(first, x1, z1); Put(first, x2, z2); return; }
            double dz = z2 - z1;
            for (int i = first; i <= last; i++)
            {
                double lo = Math.Max(z1, _zMin + i * AircraftOutline.StationM);
                double hi = Math.Min(z2, _zMin + (i + 1) * AircraftOutline.StationM);
                Put(i, x1 + (x2 - x1) * (lo - z1) / dz, lo);
                Put(i, x1 + (x2 - x1) * (hi - z1) / dz, hi);
            }
        }
    }

    /// <summary>The parts of one glTF file a silhouette needs: nodes, transforms, positions, triangles.</summary>
    private sealed class Gltf
    {
        private readonly JsonElement _root;
        private readonly string _dir;
        private readonly JsonElement[] _nodes, _accessors, _bufferViews, _meshes;
        private readonly byte[]?[] _buffers;
        // Per material: drawn with the simulator's invisible material, or only as an environment occluder.
        private readonly bool[] _unseenMaterial;
        // Per material: see-through and textured, what a blurred rotor or propeller disc is drawn with.
        private readonly bool[] _pictureMaterial;
        private readonly Dictionary<int, Dictionary<string, double[]>> _firstKeys = new();
        // Every node each animation moves, by the animation's name.
        private readonly List<(string Animation, int Node)> _animated = new();

        public Gltf(JsonElement root, string dir)
        {
            _root = root;
            _dir = dir;
            _nodes = Items("nodes");
            _accessors = Items("accessors");
            _bufferViews = Items("bufferViews");
            _meshes = Items("meshes");
            var buffers = Items("buffers");
            _buffers = new byte[]?[buffers.Length];
            for (int i = 0; i < buffers.Length; i++) _buffers[i] = LoadBuffer(buffers[i]);
            var materials = Items("materials");
            _unseenMaterial = new bool[materials.Length];
            _pictureMaterial = new bool[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                _pictureMaterial[i] = SeeThrough(materials[i]) && materials[i].TryGetProperty("pbrMetallicRoughness", out var drawn)
                                      && drawn.ValueKind == JsonValueKind.Object && drawn.TryGetProperty("baseColorTexture", out _);
                bool extended = materials[i].TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Object;
                _unseenMaterial[i] = (extended && (Enabled(ext, "ASOBO_material_invisible") || Enabled(ext, "ASOBO_material_environment_occluder")))
                                     || NotSolid(materials[i], extended ? ext : default);
            }
            ReadFirstKeys();
        }

        // A see-through material that is a DECAL (a picture laid on the surface under it), or that has no
        // colour at all (alpha 0 and no texture to give it one), is not something an aircraft can be hit
        // on: rivets and stencils lie on the airframe, and what such a material draws beyond it is light.
        // The TFDi MD-11 draws each landing light's beam (a 20 m cone ahead of the wing) with a material of
        // alpha 0, and each wingtip light's glow (12 m across) and its logo lights as decals: it measured
        // 67.5 m by 63.1 where the aircraft is 61.6 by 51.7. A see-through material that IS drawn stays: a
        // helicopter's rotor disc is one, and is the widest thing it has.
        private static bool NotSolid(JsonElement material, JsonElement extensions)
        {
            if (!SeeThrough(material)) return false;
            if (extensions.ValueKind == JsonValueKind.Object && Enabled(extensions, "ASOBO_material_geometry_decal")) return true;
            return material.TryGetProperty("pbrMetallicRoughness", out var pbr) && pbr.ValueKind == JsonValueKind.Object
                   && !pbr.TryGetProperty("baseColorTexture", out _)
                   && pbr.TryGetProperty("baseColorFactor", out var colour) && colour.ValueKind == JsonValueKind.Array
                   && colour.GetArrayLength() == 4 && colour[3].TryGetDouble(out double a) && a <= 0.0;
        }

        private static bool SeeThrough(JsonElement material)
            => material.TryGetProperty("alphaMode", out var alpha) && alpha.ValueKind == JsonValueKind.String
               && string.Equals(alpha.GetString(), "BLEND", StringComparison.Ordinal);

        // A material extension that is there and not switched off ("enabled": false).
        private static bool Enabled(JsonElement extensions, string name)
            => extensions.TryGetProperty(name, out var e)
               && !(e.ValueKind == JsonValueKind.Object && e.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.False);

        // A node's or a scene's ASOBO_unique_id, "" when it has none.
        private static string UniqueId(JsonElement element)
            => element.TryGetProperty("extensions", out var ext) && ext.ValueKind == JsonValueKind.Object
               && ext.TryGetProperty("ASOBO_unique_id", out var uid) && uid.ValueKind == JsonValueKind.Object
               && uid.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : "";

        private JsonElement[] Items(string name)
            => _root.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToArray() : Array.Empty<JsonElement>();

        private byte[]? LoadBuffer(JsonElement buffer)
        {
            if (!buffer.TryGetProperty("uri", out var u) || u.GetString() is not { Length: > 0 } uri) return null;
            const string marker = ";base64,";
            int at = uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? uri.IndexOf(marker, StringComparison.Ordinal) : -1;
            if (at >= 0) return Convert.FromBase64String(uri[(at + marker.Length)..]);
            string path = Path.Combine(_dir, Uri.UnescapeDataString(uri));
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        // The first key of every animated translation, rotation and scale, by node.
        private void ReadFirstKeys()
        {
            foreach (var animation in Items("animations"))
            {
                if (!animation.TryGetProperty("channels", out var channels) || !animation.TryGetProperty("samplers", out var samplers)) continue;
                var samplerList = samplers.EnumerateArray().ToArray();
                foreach (var channel in channels.EnumerateArray())
                {
                    if (!channel.TryGetProperty("target", out var target)
                        || !target.TryGetProperty("node", out var nodeEl) || !target.TryGetProperty("path", out var pathEl)
                        || !channel.TryGetProperty("sampler", out var samplerEl)) continue;
                    _animated.Add((animation.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "", nodeEl.GetInt32()));
                    string path = pathEl.GetString() ?? "";
                    int comps = path switch { "translation" or "scale" => 3, "rotation" => 4, _ => 0 };
                    int samplerIndex = samplerEl.GetInt32();
                    if (comps == 0 || (uint)samplerIndex >= (uint)samplerList.Length) continue;
                    var sampler = samplerList[samplerIndex];
                    if (!sampler.TryGetProperty("input", out var input) || !sampler.TryGetProperty("output", out var output)) continue;
                    double[]? times = ReadAccessor(input.GetInt32(), 1), values = ReadAccessor(output.GetInt32(), comps);
                    if (times == null || values == null || times.Length == 0) continue;
                    int key = 0;
                    for (int i = 1; i < times.Length; i++) if (times[i] < times[key]) key = i;
                    bool cubic = sampler.TryGetProperty("interpolation", out var ip) && ip.GetString() == "CUBICSPLINE";
                    int offset = cubic ? (key * 3 + 1) * comps : key * comps;
                    if (offset + comps > values.Length) continue;
                    int node = nodeEl.GetInt32();
                    if (!_firstKeys.TryGetValue(node, out var byPath)) _firstKeys[node] = byPath = new Dictionary<string, double[]>();
                    byPath.TryAdd(path, values.AsSpan(offset, comps).ToArray());
                }
            }
        }

        /// <summary>An accessor's values as doubles, <paramref name="comps"/> per element; null when unreadable.</summary>
        private double[]? ReadAccessor(int index, int comps)
        {
            if ((uint)index >= (uint)_accessors.Length) return null;
            var accessor = _accessors[index];
            if (!accessor.TryGetProperty("bufferView", out var bvEl) || (uint)bvEl.GetInt32() >= (uint)_bufferViews.Length) return null;
            var view = _bufferViews[bvEl.GetInt32()];
            int bufferIndex = view.TryGetProperty("buffer", out var bEl) ? bEl.GetInt32() : 0;
            if ((uint)bufferIndex >= (uint)_buffers.Length || _buffers[bufferIndex] is not { } bytes) return null;
            int componentType = accessor.GetProperty("componentType").GetInt32();
            int size = componentType switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => 0 };
            int declared = accessor.GetProperty("type").GetString() switch
            {
                "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 0,
            };
            if (size == 0 || declared < comps) return null;
            int count = accessor.GetProperty("count").GetInt32();
            int stride = view.TryGetProperty("byteStride", out var st) && st.GetInt32() > 0 ? st.GetInt32() : size * declared;
            long start = (view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt64() : 0)
                         + (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt64() : 0);
            if (count < 0 || start < 0 || (count > 0 && start + (long)(count - 1) * stride + (long)size * comps > bytes.Length)) return null;
            bool normalized = accessor.TryGetProperty("normalized", out var nz) && nz.ValueKind == JsonValueKind.True;
            var result = new double[count * comps];
            var span = bytes.AsSpan();
            for (int i = 0; i < count; i++)
            {
                int at = (int)(start + (long)i * stride);
                for (int c = 0; c < comps; c++, at += size)
                {
                    result[i * comps + c] = componentType switch
                    {
                        5126 => BitConverter.ToSingle(span.Slice(at, 4)),
                        5125 => BitConverter.ToUInt32(span.Slice(at, 4)),
                        5123 => normalized ? BitConverter.ToUInt16(span.Slice(at, 2)) / 65535.0 : BitConverter.ToUInt16(span.Slice(at, 2)),
                        5122 => normalized ? Math.Max(BitConverter.ToInt16(span.Slice(at, 2)) / 32767.0, -1.0) : BitConverter.ToInt16(span.Slice(at, 2)),
                        5121 => normalized ? span[at] / 255.0 : span[at],
                        _ => normalized ? Math.Max((sbyte)span[at] / 127.0, -1.0) : (sbyte)span[at],
                    };
                }
            }
            return result;
        }

        // A node's own transform as a 3x4 row-major matrix. A property the node does not declare takes
        // its animation's first key.
        private double[] Local(int index)
        {
            var node = _nodes[index];
            if (node.TryGetProperty("matrix", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() == 16)
            {
                var c = m.EnumerateArray().Select(e => e.GetDouble()).ToArray();   // column-major
                return new[] { c[0], c[4], c[8], c[12], c[1], c[5], c[9], c[13], c[2], c[6], c[10], c[14] };
            }
            _firstKeys.TryGetValue(index, out var keys);
            double[] t = Own(node, "translation", 3) ?? keys?.GetValueOrDefault("translation") ?? new[] { 0.0, 0.0, 0.0 };
            double[] q = Own(node, "rotation", 4) ?? keys?.GetValueOrDefault("rotation") ?? new[] { 0.0, 0.0, 0.0, 1.0 };
            double[] s = Own(node, "scale", 3) ?? keys?.GetValueOrDefault("scale") ?? new[] { 1.0, 1.0, 1.0 };
            double x = q[0], y = q[1], z = q[2], w = q[3];
            return new[]
            {
                (1 - 2 * (y * y + z * z)) * s[0], 2 * (x * y - z * w) * s[1], 2 * (x * z + y * w) * s[2], t[0],
                2 * (x * y + z * w) * s[0], (1 - 2 * (x * x + z * z)) * s[1], 2 * (y * z - x * w) * s[2], t[1],
                2 * (x * z - y * w) * s[0], 2 * (y * z + x * w) * s[1], (1 - 2 * (x * x + y * y)) * s[2], t[2],
            };
        }

        private static double[]? Own(JsonElement node, string name, int count)
            => node.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == count
                ? a.EnumerateArray().Select(e => e.GetDouble()).ToArray() : null;

        private static double[] Multiply(double[] a, double[] b)
        {
            var r = new double[12];
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                    r[row * 4 + col] = a[row * 4] * b[col] + a[row * 4 + 1] * b[4 + col] + a[row * 4 + 2] * b[8 + col];
                r[row * 4 + 3] += a[row * 4 + 3];
            }
            return r;
        }

        /// <summary>
        /// Every airframe mesh primitive in model space. A scene tagged with a node's unique id hangs on
        /// that node, found in this file or among <paramref name="placed"/>, the nodes of the parts read
        /// before it; the default scene otherwise sits at <paramref name="root"/>, and any other scene whose
        /// node is nowhere, or that names none, is left out.
        /// </summary>
        public List<Part> WorldParts(Regex notAirframe, Regex gearAnimation, double[] root,
            IReadOnlyDictionary<string, double[]> placed, IReadOnlyDictionary<string, double[]>? later, HashSet<string> missing,
            out Dictionary<string, double[]> nodeWorlds)
        {
            var parts = new List<Part>();
            nodeWorlds = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            var gear = new HashSet<int>();
            foreach (var (animation, node) in _animated)
                if (gearAnimation.IsMatch(animation)) gear.Add(node);
            var parentOf = new int[_nodes.Length];
            Array.Fill(parentOf, -1);
            for (int i = 0; i < _nodes.Length; i++)
                if (_nodes[i].TryGetProperty("children", out var kids))
                    foreach (var k in kids.EnumerateArray())
                        if ((uint)k.GetInt32() < (uint)parentOf.Length) parentOf[k.GetInt32()] = i;

            var roots = new List<int>();
            var scenes = Items("scenes");
            int scene = _root.TryGetProperty("scene", out var sc) ? sc.GetInt32() : 0;
            if ((uint)scene < (uint)scenes.Length && scenes[scene].TryGetProperty("nodes", out var sceneNodes))
                roots.AddRange(sceneNodes.EnumerateArray().Select(e => e.GetInt32()));
            else
                for (int i = 0; i < _nodes.Length; i++) if (parentOf[i] < 0) roots.Add(i);

            var visited = new bool[_nodes.Length];
            var stack = new Stack<(int Node, double[] Parent)>();
            // The default scene is itself hung on another part's node when it is tagged with one (both
            // scenes of the Fenix's wing fences are: left tip, right tip).
            string defaultTarget = (uint)scene < (uint)scenes.Length ? UniqueId(scenes[scene]) : "";
            bool hangs = placed.TryGetValue(defaultTarget, out var hung) || (later != null && later.TryGetValue(defaultTarget, out hung));
            double[] defaultRoot = defaultTarget.Length > 0 && hangs ? hung! : root;
            if (defaultTarget.Length > 0 && !hangs)
            {
                missing.Add(defaultTarget);
                Notes?.Invoke($"scene hung on '{defaultTarget}', which is nowhere: placed at the part's origin");
            }
            foreach (int r in roots) stack.Push((r, defaultRoot));
            var worlds = nodeWorlds;
            // What each node reached is placed in (its parent's world transform), and the skinned meshes,
            // which are placed once every node has been reached: see AddSkinned.
            var frames = new double[]?[_nodes.Length];
            var skinned = new List<(int Node, string Name)>();
            void Drain()
            {
                while (stack.Count > 0)
                {
                    var (index, parent) = stack.Pop();
                    if ((uint)index >= (uint)_nodes.Length || visited[index]) continue;
                    visited[index] = true;
                    var node = _nodes[index];
                    string name = node.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                    frames[index] = parent;
                    double[] world = Multiply(parent, Local(index));
                    if (name.Length > 0) worlds.TryAdd(name, world);
                    if (UniqueId(node) is { Length: > 0 } uid) worlds.TryAdd(uid, world);
                    if (gear.Contains(index) || notAirframe.IsMatch(name)) continue;
                    if (node.TryGetProperty("mesh", out var meshEl) && (uint)meshEl.GetInt32() < (uint)_meshes.Length)
                    {
                        if (node.TryGetProperty("skin", out _)) skinned.Add((index, name));
                        else
                        {
                            int before = parts.Count;
                            AddMesh(parts, _meshes[meshEl.GetInt32()], world);
                            if (NodeTrace is { } trace) Trace(trace, name, parts, before);
                        }
                    }
                    if (node.TryGetProperty("children", out var children))
                        foreach (var child in children.EnumerateArray()) stack.Push((child.GetInt32(), world));
                }
            }
            Drain();

            // A further scene tagged with a node's unique id hangs on that node: MSFS 2024's way of fitting
            // one part to a node of another (the Fenix's fences and sharklets on the wing's tip node).
            for (int s = 0; s < scenes.Length; s++)
            {
                if (s == scene || UniqueId(scenes[s]) is not { Length: > 0 } target
                    || !scenes[s].TryGetProperty("nodes", out var attached)) continue;
                if (!worlds.TryGetValue(target, out var at) && !placed.TryGetValue(target, out at)
                    && !(later != null && later.TryGetValue(target, out at)))
                {
                    missing.Add(target);
                    Notes?.Invoke($"scene hung on '{target}', which is nowhere: left out");
                    continue;
                }
                foreach (var r in attached.EnumerateArray()) stack.Push((r.GetInt32(), at));
                Drain();
            }

            foreach (var (index, name) in skinned)
            {
                int before = parts.Count;
                AddSkinned(parts, index, name, root, frames, parentOf, gear, notAirframe);
                if (NodeTrace is { } trace) Trace(trace, name, parts, before);
            }
            return parts;
        }

        // A skinned mesh is stored in the frame its SKELETON stands in: the parent of the node its skin
        // names as "skeleton" (the topmost of its joints when it names none). Its own node's transform is
        // not applied, as glTF says, and neither are the skin's inverse bind matrices, which in these
        // exports only undo that node transform: applied with the joints as the file poses them they put
        // the BlueBird 757's fin at its nose. Measured on 2026-11-26: the PMDG 737 keeps its whole wing
        // skin, slats, flaps and ailerons under a WING_LEFT node 2.15 m out and turned, and read as stored
        // the wing lay along the fuselage, the slats 5 m ahead of the leading edge; the PMDG 777's
        // tailplane sat at the wing, the Fenix's outboard flaps at the fuselage. Placed in the skeleton's
        // frame every one of them lands on the unskinned parts round it.
        private void AddSkinned(List<Part> parts, int index, string name, double[] root, double[]?[] frames, int[] parentOf,
            HashSet<int> gear, Regex notAirframe)
        {
            var node = _nodes[index];
            var skins = Items("skins");
            int skeleton = -1;
            if (node.TryGetProperty("skin", out var skinEl) && skinEl.TryGetInt32(out int s) && (uint)s < (uint)skins.Length)
            {
                if (skins[s].TryGetProperty("skeleton", out var sk) && sk.TryGetInt32(out int named) && (uint)named < (uint)_nodes.Length)
                    skeleton = named;
                else if (skins[s].TryGetProperty("joints", out var joints) && joints.ValueKind == JsonValueKind.Array && joints.GetArrayLength() > 0)
                {
                    var set = new HashSet<int>();
                    foreach (var j in joints.EnumerateArray()) if (j.TryGetInt32(out int joint)) set.Add(joint);
                    int top = joints[0].TryGetInt32(out int firstJoint) ? firstJoint : -1;
                    for (int guard = 0; (uint)top < (uint)parentOf.Length && set.Contains(parentOf[top]) && guard < _nodes.Length; guard++)
                        top = parentOf[top];
                    if ((uint)top < (uint)_nodes.Length) skeleton = top;
                }
            }

            // A skeleton that is, or hangs under, landing gear or ground equipment makes the mesh that too
            // (a gear strut's cables and springs).
            for (int at = skeleton, guard = 0; at >= 0 && guard < _nodes.Length; at = parentOf[at], guard++)
            {
                string ancestor = _nodes[at].TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                if (gear.Contains(at) || notAirframe.IsMatch(ancestor)) return;
            }

            double[] frame = root;
            if (skeleton >= 0 && frames[skeleton] is { } standsIn) frame = standsIn;
            else if (skeleton >= 0) Notes?.Invoke($"skinned mesh '{name}': its skeleton is in no scene, taken as stored");
            else Notes?.Invoke($"skinned mesh '{name}': its skin names no skeleton or joint, taken as stored");

            if (node.TryGetProperty("mesh", out var meshEl) && (uint)meshEl.GetInt32() < (uint)_meshes.Length)
                AddMesh(parts, _meshes[meshEl.GetInt32()], frame);
        }

        private void Trace(Action<string, string, double, double, double, double> trace, string name, List<Part> parts, int from)
        {
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            void Take(double[] xz, int vertex)
            {
                if ((uint)vertex * 2 + 1 >= (uint)xz.Length) return;
                x0 = Math.Min(x0, xz[vertex * 2]); x1 = Math.Max(x1, xz[vertex * 2]);
                z0 = Math.Min(z0, xz[vertex * 2 + 1]); z1 = Math.Max(z1, xz[vertex * 2 + 1]);
            }
            // Only the vertices a part DRAWS: the primitives of one mesh share their vertices, and the
            // others' may be a light's 25 m helper that is not in the outline at all.
            for (int p = from; p < parts.Count; p++)
            {
                if (parts[p].Triangles is { } drawn) foreach (int v in drawn) Take(parts[p].Xz, v);
                else for (int v = 0; v < parts[p].Xz.Length / 2; v++) Take(parts[p].Xz, v);
            }
            if (x0 <= x1) trace(_dir, name, x0, x1, z0, z1);
        }

        private static int AsoboInt(JsonElement asobo, string name, int fallback)
            => asobo.ValueKind == JsonValueKind.Object && asobo.TryGetProperty(name, out var v) && v.TryGetInt32(out int n) ? n : fallback;

        private void AddMesh(List<Part> parts, JsonElement mesh, double[] world)
        {
            if (!mesh.TryGetProperty("primitives", out var primitives)) return;
            // The primitives of one mesh (one per material) usually share a vertex and an index accessor.
            var positions = new Dictionary<int, double[]?>();
            var stored = new Dictionary<int, double[]?>();
            var indexLists = new Dictionary<int, double[]?>();
            foreach (var primitive in primitives.EnumerateArray())
            {
                if (!primitive.TryGetProperty("attributes", out var attributes)
                    || !attributes.TryGetProperty("POSITION", out var positionEl)) continue;
                // Never seen, so never airframe: see the class remarks.
                int material = primitive.TryGetProperty("material", out var materialEl) && materialEl.TryGetInt32(out int m)
                               && (uint)m < (uint)_unseenMaterial.Length ? m : -1;
                if (material >= 0 && _unseenMaterial[material]) continue;
                int positionIndex = positionEl.GetInt32();
                if (!positions.TryGetValue(positionIndex, out double[]? xz))
                {
                    double[]? p = ReadAccessor(positionIndex, 3);
                    if (p != null && p.Length > 0)
                    {
                        xz = new double[p.Length / 3 * 2];
                        for (int i = 0, o = 0; i + 2 < p.Length; i += 3, o += 2)
                        {
                            xz[o] = world[0] * p[i] + world[1] * p[i + 1] + world[2] * p[i + 2] + world[3];
                            xz[o + 1] = world[8] * p[i] + world[9] * p[i + 1] + world[10] * p[i + 2] + world[11];
                        }
                    }
                    positions[positionIndex] = xz;
                    stored[positionIndex] = p;
                }
                if (xz == null) continue;
                int vertices = xz.Length / 2;

                // MSFS packs a mesh's materials into shared accessors and says in extras.ASOBO_primitive
                // which slice is this primitive's: PrimitiveCount triangles from index StartIndex, each
                // index counted from vertex BaseVertexIndex. The MSFS 2020 exporter (AIG, FSLTL, IVAO)
                // writes StartIndex alone, with indices from vertex 0; the 2024 one (iniBuilds, PMDG, Fenix)
                // writes both. Read without them, a later slice is drawn with the first slice's vertices.
                JsonElement asobo = default;
                if (primitive.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object)
                    extras.TryGetProperty("ASOBO_primitive", out asobo);
                int startIndex = Math.Max(0, AsoboInt(asobo, "StartIndex", 0));
                int baseVertex = Math.Max(0, AsoboInt(asobo, "BaseVertexIndex", 0));
                int primitiveCount = AsoboInt(asobo, "PrimitiveCount", -1);

                int mode = primitive.TryGetProperty("mode", out var modeEl) ? modeEl.GetInt32() : 4;
                int[]? triangles = null;
                // The vertices this primitive is made of: [first, first + count).
                int first = baseVertex, count = Math.Max(0, AsoboInt(asobo, "VertexCount", vertices - baseVertex));
                if (mode == 4)
                {
                    double[]? raw = null;
                    if (primitive.TryGetProperty("indices", out var indicesEl))
                    {
                        int indicesIndex = indicesEl.GetInt32();
                        if (!indexLists.TryGetValue(indicesIndex, out raw)) indexLists[indicesIndex] = raw = ReadAccessor(indicesIndex, 1);
                    }
                    if (raw != null)
                    {
                        int from = Math.Min(startIndex, raw.Length);
                        int n = primitiveCount >= 0 ? (int)Math.Min((long)primitiveCount * 3, raw.Length - from) : raw.Length - from;
                        triangles = new int[n];
                        for (int i = 0; i < n; i++) triangles[i] = (int)raw[from + i] + baseVertex;
                    }
                    else
                    {
                        int n = primitiveCount >= 0 ? (int)Math.Min((long)primitiveCount * 3, Math.Max(0, vertices - baseVertex - startIndex)) : (vertices - baseVertex - startIndex) / 3 * 3;
                        triangles = new int[Math.Max(0, n)];
                        for (int i = 0; i < triangles.Length; i++) triangles[i] = baseVertex + startIndex + i;
                    }
                }

                // A handful of vertices spread wide both ways is a helper, never airframe: the plane, box
                // or single triangle an add-on hangs on a part to hold its level of detail (100 m across
                // on the iniBuilds A350's wings and the PMDG 777F). Those installed here all use the
                // invisible material and are gone already; this is for one that does not. No wing is
                // 40 m both across and along. Counted over the vertices this primitive itself draws.
                double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
                int used = 0;
                void Take(int v)
                {
                    double x = xz[v * 2], z = xz[v * 2 + 1];
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (z < z0) z0 = z;
                    if (z > z1) z1 = z;
                }
                if (triangles != null)
                {
                    var seen = new HashSet<int>();
                    foreach (int v in triangles)
                        if ((uint)v < (uint)vertices && (seen.Count > HelperMaxVertices || seen.Add(v))) Take(v);
                    used = seen.Count;
                }
                else
                {
                    for (int v = Math.Min(first, vertices); v < Math.Min((long)first + count, vertices); v++, used++) Take(v);
                }
                if (used == 0) continue;
                if (used <= HelperMaxVertices && x1 - x0 > HelperMinSpreadM && z1 - z0 > HelperMinSpreadM) continue;

                if (material >= 0 && _pictureMaterial[material] && triangles is { Length: 6 } && used == 4
                    && stored[positionIndex] is { } corners && Disc(xz, corners, triangles) is { } disc)
                {
                    parts.Add(disc);
                    continue;
                }

                if (triangles == null && (first != 0 || count < vertices))
                {
                    // Points only, and a slice of the shared vertices: keep just that slice.
                    int end = (int)Math.Min((long)first + count, vertices);
                    xz = xz.AsSpan(first * 2, Math.Max(0, end - first) * 2).ToArray();
                }
                parts.Add(new Part(xz, triangles));
            }
        }

        // A see-through, textured SQUARE of two triangles is a picture card, and on an aircraft it is a rotor
        // or a propeller turning, drawn as a blur: what it shows is the disc inside the square, never its
        // corners, and the card is often turned in its own plane (IVAO's H160 by 12 degrees, its EC175 by
        // 45), so the square read 2 to 3 m a side wider than the rotor and, turned, wider on one side than
        // the other. It is measured as the disc: the same from every angle the card is turned to.
        private static Part? Disc(double[] xz, double[] stored, int[] triangles)
        {
            Span<int> c = stackalloc int[4];
            int found = 0;
            foreach (int v in triangles)
            {
                if ((uint)v * 3 + 2 >= (uint)stored.Length) return null;
                if (!c[..found].Contains(v)) { if (found == 4) return null; c[found++] = v; }
            }
            if (found != 4) return null;
            double X(int v, int axis) => stored[v * 3 + axis];
            int c0 = c[0];
            // The corner opposite the first: the one the first shares its midpoint with.
            for (int k = 1; k < 4; k++)
            {
                int opposite = c[k], a = c[k == 1 ? 2 : 1], b = c[k == 3 ? 2 : 3];
                double side1 = 0, side2 = 0, dot = 0, apart = 0;
                for (int axis = 0; axis < 3; axis++)
                {
                    double e1 = X(a, axis) - X(c0, axis), e2 = X(b, axis) - X(c0, axis);
                    side1 += e1 * e1;
                    side2 += e2 * e2;
                    dot += e1 * e2;
                    double d = X(c0, axis) + X(opposite, axis) - X(a, axis) - X(b, axis);
                    apart += d * d;
                }
                side1 = Math.Sqrt(side1);
                side2 = Math.Sqrt(side2);
                double size = Math.Max(side1, side2);
                if (size <= 0.0 || Math.Sqrt(apart) > 0.02 * size) continue;
                if (Math.Abs(side1 - side2) > 0.05 * size || Math.Abs(dot) > 0.05 * side1 * side2) return null;

                // From above the square is a parallelogram and its disc the ellipse touching the middle of
                // each side: a line, for a propeller's upright disc.
                double cx = (xz[c0 * 2] + xz[opposite * 2]) / 2.0, cz = (xz[c0 * 2 + 1] + xz[opposite * 2 + 1]) / 2.0;
                double ux = (xz[a * 2] - xz[c0 * 2]) / 2.0, uz = (xz[a * 2 + 1] - xz[c0 * 2 + 1]) / 2.0;
                double vx = (xz[b * 2] - xz[c0 * 2]) / 2.0, vz = (xz[b * 2 + 1] - xz[c0 * 2 + 1]) / 2.0;
                var rim = new double[(DiscCorners + 1) * 2];
                rim[0] = cx;
                rim[1] = cz;
                var fan = new int[DiscCorners * 3];
                for (int i = 0; i < DiscCorners; i++)
                {
                    double t = 2.0 * Math.PI * i / DiscCorners, cos = Math.Cos(t), sin = Math.Sin(t);
                    rim[(i + 1) * 2] = cx + ux * cos + vx * sin;
                    rim[(i + 1) * 2 + 1] = cz + uz * cos + vz * sin;
                    fan[i * 3] = 0;
                    fan[i * 3 + 1] = i + 1;
                    fan[i * 3 + 2] = (i + 1) % DiscCorners + 1;
                }
                return new Part(rim, fan);
            }
            return null;
        }
    }
}
