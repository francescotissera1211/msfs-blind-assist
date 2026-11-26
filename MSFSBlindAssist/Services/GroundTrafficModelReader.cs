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
/// <item>A SKINNED mesh (a flexing wing) stores its vertices in model space already: its node's own
/// transform is not applied, as glTF itself says. The first level of detail with no skinned mesh is
/// preferred, since several models' skinned wings are stored unswept.</item>
/// <item>A node that an animation moves but that declares no transform of its own takes the animation's
/// first key (flaps and props retracted or at rest); a node that does declare one keeps it.</item>
/// <item>Landing gear never shapes an aircraft's outline from above, and its rest pose is the least
/// trustworthy part of a model (FSLTL's 777 nose gear rests 28 m below it and 6 m ahead of the nose):
/// a node a gear, wheel or steering animation moves is left out, with everything under it
/// (<see cref="GearAnimation"/>).</item>
/// <item>Ground equipment drawn inside an aircraft model (IVAO's pushback tug, fuel truck and stairs,
/// wheel chocks) is not airframe: <see cref="NotAirframe"/> names it.</item>
/// <item>The silhouette is laterally SYMMETRIC: each station takes the narrower of its two sides, which
/// drops a part parked on one side only, and only the one connected body is kept.</item>
/// </list>
/// Pure but for reading the files; never throws: a model it cannot measure is null.
/// </summary>
public static partial class GroundTrafficModelReader
{
    /// <summary>A node whose name matches, and everything under it, is ground equipment, never airframe.</summary>
    [GeneratedRegex(@"ground.?serv|^aso_|pushback|fuel.?truck|(^|_)tug|(^|_)gpu|chock|vis_blocks|stairs|catering|baggage|tow.?bar|^ramp_|marshal",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotAirframe();

    /// <summary>An animation whose name matches moves landing gear: its nodes are left out of the outline.</summary>
    [GeneratedRegex(@"gear|wheel|tire|tyre|bogie|steer", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GearAnimation();

    /// <summary>Stations this far apart (metres) or less belong to one body; a part beyond it is dropped.</summary>
    public const double MaxBodyGapM = 2.0;

    /// <summary>A measured outline outside these bounds (metres) is not an aircraft: the model is not used.</summary>
    public const double MinLengthM = 2.0, MaxLengthM = 110.0, MinHalfSpanM = 0.5, MaxHalfSpanM = 50.0;

    /// <summary>
    /// The outline of a model given its levels of detail, most detailed first: measured from the first
    /// one with no skinned mesh, else from the first. Null when none can be read.
    /// </summary>
    public static AircraftOutline? Measure(IReadOnlyList<string> lodPaths)
    {
        JsonDocument? fallback = null;
        string? fallbackPath = null;
        try
        {
            foreach (string path in lodPaths)
            {
                JsonDocument doc;
                try { doc = JsonDocument.Parse(WithoutBom(File.ReadAllBytes(path))); }
                catch (Exception) { continue; }
                bool skinned = doc.RootElement.TryGetProperty("skins", out var skins)
                               && skins.ValueKind == JsonValueKind.Array && skins.GetArrayLength() > 0;
                if (!skinned)
                {
                    using (doc)
                    {
                        var outline = MeasureDocument(doc.RootElement, path);
                        if (outline != null) return outline;
                    }
                    continue;
                }
                if (fallback == null) { fallback = doc; fallbackPath = path; }
                else doc.Dispose();
            }
            return fallback != null ? MeasureDocument(fallback.RootElement, fallbackPath!) : null;
        }
        catch (Exception) { return null; }
        finally { fallback?.Dispose(); }
    }

    private static ReadOnlyMemory<byte> WithoutBom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsMemory(3) : bytes;

    /// <summary>The outline of one glTF file; null when it cannot be read or is not aircraft-sized.</summary>
    public static AircraftOutline? Measure(string gltfPath) => Measure(new[] { gltfPath });

    private static AircraftOutline? MeasureDocument(JsonElement root, string gltfPath)
    {
        try
        {
            var gltf = new Gltf(root, Path.GetDirectoryName(gltfPath) ?? "");
            var parts = gltf.WorldParts(NotAirframe(), GearAnimation());
            return parts.Count == 0 ? null : Silhouette(parts);
        }
        catch (Exception) { return null; }
    }

    /// <summary>One mesh primitive in model space: its points as X, Z pairs and its triangles' corner indices.</summary>
    internal readonly record struct Part(double[] Xz, int[]? Triangles);

    internal static AircraftOutline? Silhouette(IReadOnlyList<Part> parts)
    {
        double zMin = double.MaxValue, zMax = double.MinValue;
        foreach (var part in parts)
            for (int i = 1; i < part.Xz.Length; i += 2)
            {
                double z = part.Xz[i];
                if (!double.IsFinite(z) || !double.IsFinite(part.Xz[i - 1])) return null;
                if (z < zMin) zMin = z;
                if (z > zMax) zMax = z;
            }
        if (zMin > zMax || zMax - zMin > 4.0 * MaxLengthM) return null;

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
        for (int i = 0; i < n; i++)
            width[i] = cells.Right[i] >= 0f && cells.Left[i] >= 0f ? Math.Min(cells.Right[i], cells.Left[i]) : -1f;

        // The one connected body: the run of stations with the most of them occupied.
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
        if (bestStart < 0) return null;

        var half = new float[bestEnd - bestStart + 1];
        for (int i = bestStart; i <= bestEnd; i++) half[i - bestStart] = Math.Max(0f, width[i]);
        double tailZ = cells.ZLow[bestStart], noseZ = cells.ZHigh[bestEnd];
        // The stations are laid out from the tail tip, so re-anchor them on it.
        double tailM = -tailZ, noseM = noseZ;
        var outline = new AircraftOutline(tailM, noseM, Resample(half, zMin + bestStart * AircraftOutline.StationM, tailZ, noseZ));
        if (outline.LengthM < MinLengthM || outline.LengthM > MaxLengthM
            || outline.HalfSpanM < MinHalfSpanM || outline.HalfSpanM > MaxHalfSpanM) return null;
        return outline;
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
            ReadFirstKeys();
        }

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

        /// <summary>Every airframe mesh primitive in model space.</summary>
        public List<Part> WorldParts(Regex notAirframe, Regex gearAnimation)
        {
            var parts = new List<Part>();
            var gear = new HashSet<int>();
            foreach (var (animation, node) in _animated)
                if (gearAnimation.IsMatch(animation)) gear.Add(node);
            var isChild = new bool[_nodes.Length];
            foreach (var node in _nodes)
                if (node.TryGetProperty("children", out var kids))
                    foreach (var k in kids.EnumerateArray())
                        if ((uint)k.GetInt32() < (uint)isChild.Length) isChild[k.GetInt32()] = true;

            var roots = new List<int>();
            var scenes = Items("scenes");
            int scene = _root.TryGetProperty("scene", out var sc) ? sc.GetInt32() : 0;
            if ((uint)scene < (uint)scenes.Length && scenes[scene].TryGetProperty("nodes", out var sceneNodes))
                roots.AddRange(sceneNodes.EnumerateArray().Select(e => e.GetInt32()));
            else
                for (int i = 0; i < _nodes.Length; i++) if (!isChild[i]) roots.Add(i);

            var identity = new[] { 1.0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
            var visited = new bool[_nodes.Length];
            var stack = new Stack<(int Node, double[] Parent)>();
            foreach (int r in roots) stack.Push((r, identity));
            while (stack.Count > 0)
            {
                var (index, parent) = stack.Pop();
                if ((uint)index >= (uint)_nodes.Length || visited[index]) continue;
                visited[index] = true;
                var node = _nodes[index];
                string name = node.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                if (gear.Contains(index) || notAirframe.IsMatch(name)) continue;
                double[] world = Multiply(parent, Local(index));
                if (node.TryGetProperty("mesh", out var meshEl) && (uint)meshEl.GetInt32() < (uint)_meshes.Length)
                    AddMesh(parts, _meshes[meshEl.GetInt32()], node.TryGetProperty("skin", out _) ? identity : world);
                if (node.TryGetProperty("children", out var children))
                    foreach (var child in children.EnumerateArray()) stack.Push((child.GetInt32(), world));
            }
            return parts;
        }

        private void AddMesh(List<Part> parts, JsonElement mesh, double[] world)
        {
            if (!mesh.TryGetProperty("primitives", out var primitives)) return;
            foreach (var primitive in primitives.EnumerateArray())
            {
                if (!primitive.TryGetProperty("attributes", out var attributes)
                    || !attributes.TryGetProperty("POSITION", out var positionEl)) continue;
                double[]? p = ReadAccessor(positionEl.GetInt32(), 3);
                if (p == null || p.Length == 0) continue;
                var xz = new double[p.Length / 3 * 2];
                for (int i = 0, o = 0; i + 2 < p.Length; i += 3, o += 2)
                {
                    xz[o] = world[0] * p[i] + world[1] * p[i + 1] + world[2] * p[i + 2] + world[3];
                    xz[o + 1] = world[8] * p[i] + world[9] * p[i + 1] + world[10] * p[i + 2] + world[11];
                }
                int mode = primitive.TryGetProperty("mode", out var modeEl) ? modeEl.GetInt32() : 4;
                int[]? triangles = null;
                if (mode == 4)
                {
                    if (primitive.TryGetProperty("indices", out var indicesEl) && ReadAccessor(indicesEl.GetInt32(), 1) is { } raw)
                        triangles = Array.ConvertAll(raw, v => (int)v);
                    else
                    {
                        triangles = new int[xz.Length / 2 / 3 * 3];
                        for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
                    }
                }
                parts.Add(new Part(xz, triangles));
            }
        }
    }
}
