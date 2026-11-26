namespace MSFSBlindAssist.Services;

// The pure rules of a thing a controller leaves to the pilot's eyes: a clearance that waits for one
// aircraft to pass ("after the A321 passing behind, push approved"). It takes effect when the pilot
// judges the named aircraft has passed. The monitor tells the pilot what those eyes would see about that
// one aircraft; it decides nothing.
internal static partial class GroundTrafficLogic
{
    /// <summary>
    /// A watched aircraft has PASSED once it is this far (feet of gap) beyond the nearest it came: clear
    /// of where it was closest by the room a pushback or a turn onto its taxiway takes, not merely one
    /// second past the closest point, where it is still right behind the tail.
    /// </summary>
    public const double PassedMarginFt = 200.0;

    /// <summary>
    /// Has the watched aircraft passed? It is moving, the gap to it is opening at
    /// <see cref="MovingAwayMinOpeningMps"/> or more, and it is <see cref="PassedMarginFt"/> beyond the
    /// nearest it has been since the pilot asked (<paramref name="minGapFt"/>). Never for one that has
    /// stopped, however far away: it has not gone.
    /// </summary>
    public static bool HasPassed(double gapFt, double minGapFt, double trafficGsKts, double openingMps)
        => trafficGsKts >= MovingTrafficKts && openingMps >= MovingAwayMinOpeningMps && gapFt >= minGapFt + PassedMarginFt;

    /// <summary>A watched aircraft missing from this many evaluations in a row is gone (a sweep can miss one).</summary>
    public const int WatchLostEvaluations = 5;

    /// <summary>
    /// After the own aircraft last moved tail-first, a pushback is still in progress for this long while it
    /// stands (the tug stops, turns, waits): the callouts for what is behind stay on with no route loaded.
    /// </summary>
    public const double PushbackHoldSec = 60.0;

    /// <summary>
    /// An aircraft standing within this of a stand's centre is PARKED on it: the stand's own radius, but
    /// never less than 15 m (the reported point is not the nose wheel) nor more than 40.
    /// </summary>
    public static bool IsOnStand(double distanceM, double standRadiusM)
        => distanceM <= Math.Clamp(standRadiusM, 15.0, 40.0);
}
