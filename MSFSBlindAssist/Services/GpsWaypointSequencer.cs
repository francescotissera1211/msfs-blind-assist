using System;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The TO-waypoint readout, and the call a pilot cannot make without being told: THAT THEY HAVE
/// JUST PASSED ONE.
///
/// ⚠️ THIS EXISTS BECAUSE "REPORT PASSING WAYPOINT" WAS THE ONE IFR INSTRUCTION THE AEROPLANE
/// COULD NOT ANSWER. A readout on a key answers "where am I going"; it does not answer "you are
/// there now", and a controller expects that call within a few seconds of the event. A sighted
/// pilot gets it for free — the flight plan sequences on the map in front of them. A blind pilot
/// was left polling a hotkey and hoping.
///
/// ⚠️ MSFSBA DOES NOT MAKE THE CALL, AND MUST NOT. Deciding what to say to a controller, and
/// when, is flying — it is the pilot's job and their judgement. What was missing was never the
/// radio call; it was the CUE the call is made from. This announces the cue and stops there,
/// which is exactly the line every other announcement in this app sits on: say what a sighted
/// pilot would have seen, then get out of the way.
///
/// THE SEQUENCE TEST IS STRUCTURAL, NEVER "the ident changed". The aeroplane publishes both ends
/// of its active leg, so a real sequence is recognisable rather than inferred: the waypoint we
/// were flying TO has become the waypoint we are flying FROM. Nothing else satisfies that.
/// A Direct-To, a plan edit, a procedure load and the first activation of a plan ALL change the
/// TO-waypoint and none of them is a passing — an "it changed" rule would announce a passing for
/// every one of them, which is worse than silence: a pilot would report passing a fix they had
/// merely re-routed to.
/// </summary>
public static class GpsWaypointSequencer
{
    private const double MetresPerNauticalMile = 1852.0;

    /// <summary>What one frame of GPS waypoint data means, once compared with the frame before.</summary>
    public readonly struct Reading
    {
        /// <summary>The waypoint being flown to, empty when there is none.</summary>
        public string NextId { get; init; }
        /// <summary>Distance to it, nautical miles.</summary>
        public double DistanceNm { get; init; }
        /// <summary>Magnetic bearing to it, degrees.</summary>
        public double BearingDeg { get; init; }
        /// <summary>Time to it in seconds, 0 when stopped (the navigator publishes 0 below 1 knot).</summary>
        public double EteSeconds { get; init; }
        /// <summary>True when a flight plan is active at all.</summary>
        public bool HasPlan { get; init; }
        /// <summary>The waypoint just passed, empty unless this frame IS a passing.</summary>
        public string PassedId { get; init; }
    }

    /// <summary>
    /// Reads one frame against the previous TO-waypoint. <paramref name="previousNextId"/> is
    /// null before the first frame, which is what makes the first one a BASELINE: connecting to
    /// an aeroplane already established on a leg must not announce a passing that happened
    /// before MSFSBA was watching, the same rule every other monitor here follows.
    /// </summary>
    public static Reading Read(SimConnectManager.GpsWaypointData data, string? previousNextId)
    {
        string next = (data.NextId ?? string.Empty).Trim();
        string prev = (data.PrevId ?? string.Empty).Trim();

        bool passed =
            previousNextId != null &&                       // not the baseline frame
            previousNextId.Length > 0 &&
            next.Length > 0 &&
            !string.Equals(next, previousNextId, StringComparison.OrdinalIgnoreCase) &&
            data.PrevValid > 0.5 &&
            string.Equals(prev, previousNextId, StringComparison.OrdinalIgnoreCase);

        return new Reading
        {
            NextId = next,
            DistanceNm = data.DistanceMeters / MetresPerNauticalMile,
            BearingDeg = data.BearingDegrees,
            EteSeconds = data.EteSeconds,
            HasPlan = data.IsActiveFlightPlan > 0.5,
            PassedId = passed ? prev : string.Empty
        };
    }

    /// <summary>
    /// The passing cue. Names the fix passed and the one now being flown to, because "passing
    /// SOXOM" and "next VEKIN" are the two halves of the same position report and asking for
    /// the second on a separate keypress wastes the moment. No bearing and no time: this fires
    /// unprompted, and a pilot who wants the rest presses the key.
    /// </summary>
    public static string ComposePassing(Reading r, Func<double, string>? distance = null)
    {
        if (r.PassedId.Length == 0) return string.Empty;
        string next = r.NextId.Length > 0
            ? $" Next {Spell(r.NextId)}, {(distance ?? DefaultDistance)(r.DistanceNm)}."
            : string.Empty;
        return $"Passing {Spell(r.PassedId)}.{next}";
    }

    /// <summary>
    /// The on-demand readout. Everything a position report needs in one sentence: which fix,
    /// how far, which way, and how long — the last being what "estimating SOXOM at 34" is
    /// built from, and free here because the navigator already publishes it.
    /// </summary>
    public static string ComposeReadout(Reading r, Func<double, string>? distance = null)
    {
        if (!r.HasPlan || r.NextId.Length == 0) return "No active waypoint.";

        string text = $"{Spell(r.NextId)}, {(distance ?? DefaultDistance)(r.DistanceNm)}, bearing {r.BearingDeg:000}";
        if (r.EteSeconds >= 1)
        {
            int minutes = (int)Math.Round(r.EteSeconds / 60.0);
            text += minutes >= 1
                ? $", {minutes} minute{(minutes == 1 ? "" : "s")}"
                : ", less than a minute";
        }
        return text + ".";
    }

    private static string DefaultDistance(double nm) =>
        nm < 10 ? $"{nm:0.0} miles" : $"{nm:0} miles";

    /// <summary>
    /// A waypoint ident is SPELT, letter by letter, exactly as a pilot reads one back to a
    /// controller. "SOXOM" run together through a speech synthesiser is a noise; S O X O M is a
    /// fix. Only for the ident-shaped ones — an airport or a named leg the navigator generated
    /// ("HDG 071") is prose and stays prose.
    /// </summary>
    private static string Spell(string ident)
    {
        if (ident.Length == 0 || ident.Length > 5) return ident;
        foreach (char c in ident)
            if (!char.IsLetterOrDigit(c)) return ident;
        return string.Join(" ", ident.ToUpperInvariant().ToCharArray());
    }
}
