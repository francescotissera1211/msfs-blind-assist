namespace MSFSBlindAssist.Services;

/// <summary>How an <see cref="AircraftHull"/> was obtained, most exact first.</summary>
public enum AircraftHullSource
{
    /// <summary>Measured from the installed model's own geometry (<see cref="GroundTrafficModelReader"/>).</summary>
    Model,
    /// <summary>The type's published length and wing span (<see cref="GroundTrafficAircraftSizes"/>).</summary>
    Table,
    /// <summary>Scaled from the wing span the simulator reports.</summary>
    WingSpan,
    /// <summary>Nothing known: the widebody the monitor's distances always assumed.</summary>
    Default,
}

/// <summary>
/// An aircraft's outline seen from above, as line segments in ITS OWN frame: X metres to the right of
/// the point the simulator reports as its position, Z metres ahead of it. The ground-traffic monitor
/// measures between two of these (<see cref="GroundTrafficLogic.HullGapM"/>), so a distance is nose to
/// tail, tail to nose or wingtip to wingtip — whichever parts are nearest — and never centre to centre.
/// <para>A measured hull (<see cref="FromOutline"/>) is the model's real silhouette. Without one
/// (<see cref="FromSize"/>) it is a cross: the fuselage line and a straight wing line through the
/// reported point, with the nose <see cref="NoseFraction"/> of the length ahead of it.</para>
/// </summary>
public sealed class AircraftHull
{
    /// <summary>
    /// Where the simulator's reported point sits along the fuselage when the model was not measured, as
    /// the fraction of the length ahead of it. Over the AIG and FSLTL airliner models installed on
    /// 2026-11-26 it runs 0.43 to 0.51 (their origin is near the wing).
    /// </summary>
    public const double NoseFraction = 0.46;

    public readonly record struct Segment(double X1, double Z1, double X2, double Z2);

    public IReadOnlyList<Segment> Segments { get; }
    /// <summary>Metres from the reported point to the foremost point.</summary>
    public double NoseM { get; }
    /// <summary>Metres from the reported point to the rearmost point.</summary>
    public double TailM { get; }
    /// <summary>Half the widest part, metres.</summary>
    public double HalfSpanM { get; }
    public AircraftHullSource Source { get; }
    /// <summary>The ICAO type the size came from, "" when measured or unknown.</summary>
    public string Icao { get; }

    public double LengthM => NoseM + TailM;

    private AircraftHull(IReadOnlyList<Segment> segments, double noseM, double tailM, double halfSpanM,
        AircraftHullSource source, string icao)
    {
        Segments = segments;
        NoseM = noseM;
        TailM = tailM;
        HalfSpanM = halfSpanM;
        Source = source;
        Icao = icao;
    }

    /// <summary>The hull assumed when nothing is known about an aircraft (<see cref="GroundTrafficAircraftSizes.Default"/>).</summary>
    public static readonly AircraftHull Default = FromSize(GroundTrafficAircraftSizes.Default);

    /// <summary>The cross hull of a length and wing span.</summary>
    public static AircraftHull FromSize(AircraftSize size)
    {
        double nose = size.LengthM * NoseFraction, tail = size.LengthM - nose, half = size.WingspanM / 2.0;
        var segments = new[] { new Segment(0, -tail, 0, nose), new Segment(-half, 0, half, 0) };
        var source = size.Source switch
        {
            AircraftSizeSource.Table => AircraftHullSource.Table,
            AircraftSizeSource.WingSpan => AircraftHullSource.WingSpan,
            _ => AircraftHullSource.Default,
        };
        return new AircraftHull(segments, nose, tail, half, source, size.Icao);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AircraftOutline, AircraftHull> ByOutline = new();

    /// <summary>The measured hull of a model: the closed outline of its silhouette. One hull per outline.</summary>
    public static AircraftHull FromOutline(AircraftOutline outline) => ByOutline.GetValue(outline, Build);

    private static AircraftHull Build(AircraftOutline outline)
    {
        var p = outline.Polygon;
        var segments = new Segment[p.Count];
        for (int i = 0; i < p.Count; i++)
        {
            var (x1, z1) = p[i];
            var (x2, z2) = p[(i + 1) % p.Count];
            segments[i] = new Segment(x1, z1, x2, z2);
        }
        return new AircraftHull(segments, outline.NoseM, outline.TailM, outline.HalfSpanM, AircraftHullSource.Model, "");
    }

    /// <summary>
    /// How far the hull reaches from its reported point in a direction given relative to its own nose
    /// (degrees, 0 = ahead, 90 = to its right): its extent along that axis, never negative.
    /// </summary>
    public double ExtentToward(double relativeDeg)
    {
        double r = relativeDeg * Math.PI / 180.0, sx = Math.Sin(r), cz = Math.Cos(r), best = 0.0;
        foreach (var s in Segments)
        {
            best = Math.Max(best, s.X1 * sx + s.Z1 * cz);
            best = Math.Max(best, s.X2 * sx + s.Z2 * cz);
        }
        return best;
    }
}

internal static partial class GroundTrafficLogic
{
    /// <summary>
    /// The gap in metres between two aircraft outlines: the distance between their nearest points, 0 when
    /// they touch or overlap. <paramref name="rx"/>, <paramref name="ry"/> are the other aircraft's
    /// reported point relative to the own one in metres east and north (<see cref="ToLocal"/>); the
    /// headings are true.
    /// </summary>
    public static double HullGapM(AircraftHull own, double ownHeadingTrueDeg,
        AircraftHull other, double otherHeadingTrueDeg, double rx, double ry)
    {
        var a = Place(own, ownHeadingTrueDeg, 0.0, 0.0);
        var b = Place(other, otherHeadingTrueDeg, rx, ry);
        double best = double.MaxValue;
        foreach (var s in a)
        {
            foreach (var t in b)
            {
                double d = SegmentDistance(s.X1, s.Z1, s.X2, s.Z2, t.X1, t.Z1, t.X2, t.Z2);
                if (d < best)
                {
                    best = d;
                    if (best <= 0.0) return 0.0;
                }
            }
        }
        return best == double.MaxValue ? Math.Sqrt(rx * rx + ry * ry) : best;
    }

    // A hull's segments in the local east/north frame: (X1, Z1) is then (east, north).
    private static AircraftHull.Segment[] Place(AircraftHull hull, double headingTrueDeg, double east, double north)
    {
        double h = headingTrueDeg * Math.PI / 180.0, sin = Math.Sin(h), cos = Math.Cos(h);
        var placed = new AircraftHull.Segment[hull.Segments.Count];
        for (int i = 0; i < placed.Length; i++)
        {
            var s = hull.Segments[i];
            placed[i] = new AircraftHull.Segment(
                east + s.Z1 * sin + s.X1 * cos, north + s.Z1 * cos - s.X1 * sin,
                east + s.Z2 * sin + s.X2 * cos, north + s.Z2 * cos - s.X2 * sin);
        }
        return placed;
    }

    /// <summary>The distance between two line segments in a plane: 0 when they cross or touch.</summary>
    internal static double SegmentDistance(double ax, double ay, double bx, double by,
        double cx, double cy, double dx, double dy)
    {
        if (SegmentsCross(ax, ay, bx, by, cx, cy, dx, dy)) return 0.0;
        return Math.Min(
            Math.Min(PointSegmentDistance(ax, ay, cx, cy, dx, dy), PointSegmentDistance(bx, by, cx, cy, dx, dy)),
            Math.Min(PointSegmentDistance(cx, cy, ax, ay, bx, by), PointSegmentDistance(dx, dy, ax, ay, bx, by)));
    }

    private static double PointSegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
        double t = len2 < 1e-12 ? 0.0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0.0, 1.0);
        double cx = ax + dx * t, cy = ay + dy * t;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }

    private static bool SegmentsCross(double ax, double ay, double bx, double by,
        double cx, double cy, double dx, double dy)
    {
        double d1 = Cross(cx, cy, dx, dy, ax, ay), d2 = Cross(cx, cy, dx, dy, bx, by);
        double d3 = Cross(ax, ay, bx, by, cx, cy), d4 = Cross(ax, ay, bx, by, dx, dy);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Cross(double ax, double ay, double bx, double by, double px, double py)
        => (bx - ax) * (py - ay) - (by - ay) * (px - ax);
}
