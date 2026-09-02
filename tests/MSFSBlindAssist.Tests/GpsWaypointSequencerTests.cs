using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The waypoint-passing call. Every test here is about the ONE question that decides whether
/// this feature helps or misleads: is this frame a passing, or merely a change?
///
/// A pilot acts on the call. Announcing a passing for a Direct-To or a plan activation would
/// have them report passing a fix they had only re-routed to, which is worse than the silence
/// this replaces.
/// </summary>
public class GpsWaypointSequencerTests
{
    private static SimConnectManager.GpsWaypointData Frame(
        string next, string prev, bool prevValid = true, double distanceMetres = 18520,
        double bearing = 87, double ete = 360, bool plan = true, bool directTo = false)
        => new()
        {
            NextId = next,
            PrevId = prev,
            DistanceMeters = distanceMetres,
            BearingDegrees = bearing,
            EteSeconds = ete,
            IsActiveFlightPlan = plan ? 1 : 0,
            IsDirectTo = directTo ? 1 : 0,
            PrevValid = prevValid ? 1 : 0
        };

    [Fact]
    public void TheFirstFrameIsABaselineAndNeverAPassing()
    {
        // Connecting to an aeroplane already established on a leg. The passing, if there was
        // one, happened before MSFSBA was watching.
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM"), previousNextId: null);
        Assert.Equal("", r.PassedId);
        Assert.Equal("VEKIN", r.NextId);
    }

    [Fact]
    public void SequencingOntoTheNextLegIsAPassing()
    {
        // The waypoint we were flying TO has become the waypoint we are flying FROM.
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM"), previousNextId: "SOXOM");
        Assert.Equal("SOXOM", r.PassedId);
    }

    [Fact]
    public void AnUnchangedFrameIsNotAPassing()
    {
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "TOPGI"), previousNextId: "SOXOM");
        Assert.Equal("", r.PassedId);
    }

    [Fact]
    public void ADirectToIsNotAPassing()
    {
        // ⚠️ THE WHOLE POINT. A Direct-To changes the TO-waypoint exactly like a sequence does,
        // so "the ident changed" would announce one. It is not a passing: the fix we were
        // flying to has NOT become the fix we are flying from — it has simply been abandoned.
        var r = GpsWaypointSequencer.Read(Frame("KUGEL", "TOPGI", directTo: true), previousNextId: "SOXOM");
        Assert.Equal("", r.PassedId);
    }

    [Fact]
    public void ActivatingAPlanIsNotAPassing()
    {
        // Empty to something. Nothing was passed; a route was loaded.
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", ""), previousNextId: "");
        Assert.Equal("", r.PassedId);
    }

    [Fact]
    public void ADiscontinuityIsNotAPassing()
    {
        // The navigator clears PREV VALID across a discontinuity, so the previous leg is not a
        // fix that was overflown and must not be reported as one.
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM", prevValid: false), previousNextId: "SOXOM");
        Assert.Equal("", r.PassedId);
    }

    [Fact]
    public void ThePassingCallNamesBothTheFixPassedAndTheOneNowActive()
    {
        // Two halves of one position report. Asking for the second on a separate keypress
        // wastes the moment the call has to be made in.
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM", distanceMetres: 25928), previousNextId: "SOXOM");
        string said = GpsWaypointSequencer.ComposePassing(r);
        Assert.Contains("Passing S O X O M", said);
        Assert.Contains("Next V E K I N", said);
        Assert.Contains("14 miles", said);
    }

    [Fact]
    public void ANonPassingComposesNothingAtAll()
    {
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "TOPGI"), previousNextId: "SOXOM");
        Assert.Equal("", GpsWaypointSequencer.ComposePassing(r));
    }

    [Fact]
    public void TheReadoutCarriesFixDistanceBearingAndTime()
    {
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "TOPGI", distanceMetres: 25928, bearing: 87, ete: 360),
                                          previousNextId: "SOXOM");
        string said = GpsWaypointSequencer.ComposeReadout(r);
        Assert.Contains("S O X O M", said);
        Assert.Contains("14 miles", said);
        Assert.Contains("bearing 087", said);   // padded — "bearing 87" reads as a different number
        Assert.Contains("6 minutes", said);
    }

    [Fact]
    public void TheReadoutSaysSoWhenThereIsNoWaypoint()
    {
        var r = GpsWaypointSequencer.Read(Frame("", "", plan: false), previousNextId: null);
        Assert.Equal("No active waypoint.", GpsWaypointSequencer.ComposeReadout(r));
    }

    [Fact]
    public void TimeIsOmittedWhenTheNavigatorPublishesZero()
    {
        // Stopped on the ground the navigator writes ETE 0 (it divides by ground speed and
        // refuses below a knot). "0 minutes" would be a reading; it is an absence.
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "", ete: 0), previousNextId: "SOXOM");
        string said = GpsWaypointSequencer.ComposeReadout(r);
        Assert.DoesNotContain("minute", said);
    }

    [Fact]
    public void AnIdentIsSpeltButProseIsNot()
    {
        // "SOXOM" run together through a synthesiser is a noise; a generated leg name is a
        // phrase and must survive intact.
        var fix = GpsWaypointSequencer.Read(Frame("SOXOM", ""), previousNextId: "SOXOM");
        Assert.Contains("S O X O M", GpsWaypointSequencer.ComposeReadout(fix));

        var leg = GpsWaypointSequencer.Read(Frame("HDG 071", ""), previousNextId: "HDG 071");
        Assert.Contains("HDG 071", GpsWaypointSequencer.ComposeReadout(leg));
    }

    [Fact]
    public void AnUncomputedLegNamesTheFixAndRefusesToInventTheNumbers()
    {
        // ⚠️ FOUND BY PRESSING THE KEY, NOT BY READING THE CODE. On the ground at VCBI with the
        // whole route loaded and the TO waypoint correctly reading LIKRA, every geometry field
        // the navigator publishes was exactly zero - distance, bearing, desired track,
        // cross-track and ETE - because the G1000's LNAV does not begin computing until it is
        // tracking a leg. The readout said "LIKRA, 0.0 miles, bearing 000", which a pilot hears
        // as being ON TOP OF THE FIX: the opposite of the truth, in the voice of a measurement.
        var r = GpsWaypointSequencer.Read(Frame("LIKRA", "VCBI", distanceMetres: 0, bearing: 0, ete: 0),
                                          previousNextId: "LIKRA");
        string said = GpsWaypointSequencer.ComposeReadout(r);

        Assert.Equal("L I K R A, distance not computed.", said);
        Assert.DoesNotContain("miles", said);
        Assert.DoesNotContain("bearing", said);
    }

    [Fact]
    public void APassingOntoAnUncomputedLegNamesTheNextFixWithoutADistance()
    {
        var r = GpsWaypointSequencer.Read(Frame("VEKIN", "SOXOM", distanceMetres: 0),
                                          previousNextId: "SOXOM");
        string said = GpsWaypointSequencer.ComposePassing(r);

        Assert.Equal("Passing S O X O M. Next V E K I N.", said);
        Assert.DoesNotContain("miles", said);
    }

    [Fact]
    public void BearingZeroIsStillARealBearingWhenThereIsADistanceBehindIt()
    {
        // Distance is the discriminator and bearing must never be: due north is 000, and
        // suppressing a reading because of it would lose a legitimate one.
        var r = GpsWaypointSequencer.Read(Frame("SOXOM", "", distanceMetres: 9260, bearing: 0),
                                          previousNextId: "SOXOM");
        Assert.Contains("bearing 000", GpsWaypointSequencer.ComposeReadout(r));
    }

    [Fact]
    public void DistanceGainsADecimalOnlyWhenItIsClose()
    {
        var near = GpsWaypointSequencer.Read(Frame("SOXOM", "", distanceMetres: 5556), previousNextId: "SOXOM");
        Assert.Contains("3.0 miles", GpsWaypointSequencer.ComposeReadout(near));

        var far = GpsWaypointSequencer.Read(Frame("SOXOM", "", distanceMetres: 185200), previousNextId: "SOXOM");
        Assert.Contains("100 miles", GpsWaypointSequencer.ComposeReadout(far));
    }
}
