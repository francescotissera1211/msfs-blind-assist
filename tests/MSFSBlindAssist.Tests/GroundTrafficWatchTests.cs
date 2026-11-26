// The aircraft the pilot asked to be told about in the traffic window ("Tell me when it has passed": "after
// the A321 passing behind, push approved"); the callouts of a pushback with no route loaded; and the
// aircraft parked on a stand, which is not called out. Driven through the headless harness with real
// sizes (A320 37.57 x 35.80 m).
//
// Geometry: the equator, the own aircraft heading east along the x axis; north is to its left.

using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using static MSFSBlindAssist.Tests.GroundTrafficHarness;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficWatchTests
{
    private const double Kt = 0.514444;

    private static AiTrafficDataEventArgs A320(uint id, double eastM, double northM, double gsKts, double headingDeg = 90.0)
    {
        var ac = Ac(id, eastM, northM, gsKts, "Delta", "DAL" + id, headingDeg);
        ac.AircraftType = "A320";
        return ac;
    }

    private static GroundTrafficHarness OnRoute()
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(8000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        h.Sim.Position = Own(0, 0);
        return h;
    }

    // Taxi guidance not engaged: every automatic callout is held back, for that reason alone.
    private static GroundTrafficHarness Idle()
    {
        var h = new GroundTrafficHarness { Context = null };
        h.UseRealSizes("A320", 117.5);
        h.Monitor.SuppressCheck = () => true;
        h.Monitor.IdleCheck = () => true;
        h.Sim.Position = Own(0, 0);
        return h;
    }

    private static string All(GroundTrafficHarness h) => string.Join(" | ", h.Transcript);

    // One simulated second: the own aircraft and one traffic aircraft moved, then the tick.
    private static void Step(GroundTrafficHarness h, ref double ownEast, double ownKts, AiTrafficDataEventArgs ac,
        double east, double north, double gsKts, double? headingDeg = null)
    {
        ownEast += ownKts * Kt;
        ac.Longitude = east * M; ac.Latitude = north * M; ac.GroundSpeedKnots = gsKts;
        if (headingDeg is double hdg) ac.HeadingMagnetic = hdg;
        h.Sim.Position = Own(ownEast, Math.Abs(ownKts));
        h.Tick();
    }

    // ── Asking, and what is not said ─────────────────────────────────────────────────────────────

    [Fact]
    public void An_aircraft_the_monitor_never_sees_cannot_be_watched()
    {
        var h = OnRoute();
        h.Monitor.WatchUntilPassed(77, "XYZ 77");
        h.Tick(12);

        Assert.True(h.Said.All.Any(m => m == "Cannot watch XYZ 77: not found on the ground nearby."), All(h));
        Assert.Null(h.Monitor.WatchedObjectId);
    }

    [Fact]
    public void A_watched_aircraft_that_goes_out_of_range_ends_the_watch()
    {
        var h = OnRoute();
        h.Sim.Traffic.Add(A320(1, 200, 0, 0));
        h.Monitor.WatchUntilPassed(1, "DAL 1");
        h.Tick(3);
        h.Sim.Traffic.Clear();
        h.Tick(12);

        Assert.True(h.Said.All.Any(m => m == "No longer watching Delta A320: out of range."), All(h));
        Assert.Null(h.Monitor.WatchedObjectId);
    }

    [Fact]
    public void Stop_watching_says_so_once()
    {
        var h = OnRoute();
        h.Sim.Traffic.Add(A320(1, 200, 0, 0));
        h.Monitor.WatchUntilPassed(1, "DAL 1");
        h.Tick(3);

        Assert.True(h.Monitor.StopWatching());
        Assert.Equal("Stopped watching Delta A320.", h.Said.Interrupts[^1]);
        Assert.Null(h.Monitor.WatchedObjectId);
        Assert.False(h.Monitor.StopWatching());
    }

    [Fact]
    public void The_watched_aircraft_has_its_own_lines_and_the_summary_begins_with_it()
    {
        var h = OnRoute();
        var ac = A320(1, 200, 0, 12);
        h.Sim.Traffic.Add(ac);
        h.Monitor.WatchUntilPassed(1, "DAL 1");
        double own = 0, its = 200;
        for (int s = 0; s < 4; s++) { its += 12 * Kt; Step(h, ref own, 12, ac, its, 0, 12); }

        Assert.StartsWith("Watching Delta A320: ahead, ", h.Said.All[0]);
        // Never the ordinary awareness or "on your route" line for it as well.
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Delta A320, ", StringComparison.Ordinal) || m.Contains("on your route", StringComparison.Ordinal));
        string summary = h.Monitor.GetNearestTrafficSummary();
        Assert.StartsWith("Watching Delta A320: ahead, ", summary);
        Assert.Contains(", 12 knots. 1 aircraft nearby.", summary);
    }

    [Fact]
    public void With_taxi_guidance_not_engaged_the_watched_aircraft_is_still_told_and_nothing_else_is()
    {
        // No route, no guidance: nothing automatic is spoken ... until the pilot asks about one aircraft.
        var h = Idle();
        h.Sim.Traffic.Add(A320(1, 200, 0, 0));
        h.Sim.Traffic.Add(A320(2, 60, 60, 0));           // another, nearby: never mentioned
        h.Tick(5);
        Assert.Empty(h.Said.All);

        h.Monitor.WatchUntilPassed(1, "DAL 1");
        h.Tick(5);
        Assert.True(h.Said.All.Count == 1 && h.Said.All[0].StartsWith("Watching Delta A320: ahead, ", StringComparison.Ordinal), All(h));
    }

    [Fact]
    public void The_takeoff_roll_ends_whatever_was_asked()
    {
        var h = OnRoute();
        h.Sim.Traffic.Add(A320(1, 200, 0, 0));
        h.Monitor.WatchUntilPassed(1, "DAL 1");
        h.Tick(3);
        h.Monitor.SuppressCheck = () => true;            // takeoff assist: not the idle reason
        h.Monitor.IdleCheck = () => false;
        h.Tick(2);

        Assert.Null(h.Monitor.WatchedObjectId);
    }

    // ── Tell me when it has passed ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_watched_aircraft_is_clear_once_it_is_200_feet_beyond_where_it_was_nearest()
    {
        // Parked on the stand, no route: "after the A320 passing behind, push approved". It taxies along
        // the taxilane 80 m behind the tail, from the left to the right.
        var h = Idle();
        var ac = A320(1, -100, 300, 10, headingDeg: 180);
        h.Sim.Traffic.Add(ac);
        h.Monitor.WatchUntilPassed(1, "DAL 1");
        double own = 0, north = 300, minGapM = double.MaxValue, gapAtClearM = double.NaN;
        var hull = AircraftHull.FromSize(GroundTrafficAircraftSizes.Resolve("A320", null, null, 0));
        for (int s = 0; s < 90 && double.IsNaN(gapAtClearM); s++)
        {
            north -= 10 * Kt;
            Step(h, ref own, 0, ac, -100, north, 10, headingDeg: 180);
            double gap = GroundTrafficLogic.HullGapM(hull, 90, hull, 180, -100, north);
            minGapM = Math.Min(minGapM, gap);
            if (h.Said.All.Any(m => m.StartsWith("Clear of Delta A320: ", StringComparison.Ordinal))) gapAtClearM = gap;
        }

        Assert.True(h.Said.All[0].StartsWith("Watching Delta A320: ", StringComparison.Ordinal), All(h));
        Assert.False(double.IsNaN(gapAtClearM), All(h));
        Assert.True(gapAtClearM >= minGapM + 200 * 0.3048 - 1.0, $"clear at {gapAtClearM:0} m, nearest {minGapM:0} m");
        Assert.Contains(h.Said.All, m => m.StartsWith("Clear of Delta A320: behind and to the right, ", StringComparison.Ordinal)
            && m.EndsWith(", moving away.", StringComparison.Ordinal));
        Assert.Null(h.Monitor.WatchedObjectId);
    }

    [Fact]
    public void A_watched_aircraft_that_stops_behind_the_tail_is_not_clear()
    {
        var h = Idle();
        var ac = A320(1, -100, 300, 10, headingDeg: 180);
        h.Sim.Traffic.Add(ac);
        h.Monitor.WatchUntilPassed(1, "DAL 1");
        double own = 0, north = 300;
        for (int s = 0; s < 58 && north > 5; s++) { north -= 10 * Kt; Step(h, ref own, 0, ac, -100, north, 10, headingDeg: 180); }
        for (int s = 0; s < 30; s++) Step(h, ref own, 0, ac, -100, north, 0, headingDeg: 180);

        Assert.True(h.Said.All.Any(m => m.StartsWith("Delta A320 has stopped, behind", StringComparison.Ordinal)), All(h));
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Clear of", StringComparison.Ordinal));
        Assert.NotNull(h.Monitor.WatchedObjectId);

        for (int s = 0; s < 40; s++) { north -= 10 * Kt; Step(h, ref own, 0, ac, -100, north, 10, headingDeg: 180); }
        Assert.True(h.Said.All.Any(m => m.StartsWith("Delta A320 is moving, ", StringComparison.Ordinal)), All(h));
        Assert.True(h.Said.All.Any(m => m.StartsWith("Clear of Delta A320: ", StringComparison.Ordinal)), All(h));
    }

    // ── Pushed back with no route ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_pushback_with_no_route_loaded_still_hears_Slow_down_and_Stop_for_what_is_behind()
    {
        // Taxi guidance not engaged, nothing asked: a tug moves the aircraft tail-first toward an A320
        // 150 m behind. Until 2026-11-26 nothing at all was said without a route.
        var h = Idle();
        h.Sim.Traffic.Add(A320(1, -150, 0, 0));
        h.Sim.Traffic.Add(A320(2, 0, 120, 0));            // beside the stand: no business of the pushback
        double own = 0;
        for (int s = 0; s < 70; s++) { own -= 3 * Kt; h.Sim.Position = Own(own, 3); h.Tick(); }

        Assert.True(h.Said.All.Any(m => m.StartsWith("Slow down, Delta A320 behind", StringComparison.Ordinal)), All(h));
        Assert.True(h.Said.All.Any(m => m.StartsWith("Stop, Delta A320 very close, behind", StringComparison.Ordinal)), All(h));
        Assert.All(h.Said.All, m => Assert.True(m.StartsWith("Slow down", StringComparison.Ordinal) || m.StartsWith("Stop", StringComparison.Ordinal), m));
    }

    [Fact]
    public void Taxiing_forward_with_no_route_is_still_silent()
    {
        var h = Idle();
        h.Sim.Traffic.Add(A320(1, 150, 0, 0));
        double own = 0;
        for (int s = 0; s < 20; s++) { own += 5 * Kt; h.Sim.Position = Own(own, 5); h.Tick(); }

        Assert.Empty(h.Said.All);
    }

    // ── Parked on a stand ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_aircraft_parked_on_a_stand_beside_the_route_is_not_called_out(bool onStand)
    {
        // Parked nose-in 90 m to the right of the route. With no stand under it, it is an aircraft stopped
        // on the apron and is pointed out once; on a stand it is scenery.
        var h = OnRoute();
        if (onStand) h.Monitor.StandSupplier = _ => new[] { (-90 * M, 400 * M, 20.0) };
        var ac = A320(1, 400, -90, 0, headingDeg: 180);
        h.Sim.Traffic.Add(ac);
        double own = 0;
        for (int s = 0; s < 100; s++) Step(h, ref own, 12, ac, 400, -90, 0);

        Assert.Equal(!onStand, h.Said.All.Any(m => m.StartsWith("Delta A320, ", StringComparison.Ordinal)));
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Stop", StringComparison.Ordinal) || m.StartsWith("Slow down", StringComparison.Ordinal));
        // It is still in the summary while it is in range: the pilot can always ask.
        h.Sim.Position = Own(400, 0);
        h.Tick(2);
        Assert.Contains("Delta A320", h.Monitor.GetNearestTrafficSummary());
    }

    [Fact]
    public void The_stands_are_asked_for_once_per_airport_and_again_after_a_database_switch()
    {
        var h = OnRoute();
        int asked = 0;
        h.Monitor.StandSupplier = _ => { asked++; return new[] { (-90 * M, 400 * M, 20.0) }; };
        var ac = A320(1, 400, -90, 0, headingDeg: 180);
        h.Sim.Traffic.Add(ac);
        double own = 300;
        for (int s = 0; s < 6; s++) Step(h, ref own, 5, ac, 400, -90, 0);
        Assert.Equal(1, asked);

        // A database switch (ClearRunwayCache): the next aircraft judged has the stands asked for again.
        h.Monitor.ClearRunwayCache();
        var next = A320(2, 400, 80, 0, headingDeg: 0);
        h.Sim.Traffic.Add(next);
        for (int s = 0; s < 6; s++) Step(h, ref own, 5, next, 400, 80, 0);
        Assert.Equal(2, asked);
    }

    [Fact]
    public void An_aircraft_on_a_stand_whose_tail_is_in_the_way_is_still_Stop()
    {
        var h = OnRoute();
        h.Monitor.StandSupplier = _ => new[] { (-30 * M, 400 * M, 20.0) };
        var ac = A320(1, 400, -30, 0, headingDeg: 180);       // its tail 9.7 m from the route line
        h.Sim.Traffic.Add(ac);
        double own = 0;
        for (int s = 0; s < 70; s++) Step(h, ref own, 10, ac, 400, -30, 0);

        Assert.True(h.Said.All.Any(m => m.StartsWith("Stop, Delta A320 very close", StringComparison.Ordinal)), All(h));
    }

    [Fact]
    public void An_aircraft_that_leaves_its_stand_is_called_out_again()
    {
        var h = OnRoute();
        h.Monitor.StandSupplier = _ => new[] { (-90 * M, 200 * M, 20.0) };
        var ac = A320(1, 200, -90, 0, headingDeg: 180);
        h.Sim.Traffic.Add(ac);
        double own = 0, north = -90;
        for (int s = 0; s < 10; s++) Step(h, ref own, 5, ac, 200, north, 0);
        Assert.Empty(h.Said.All);
        // Pushed back toward the route, tail-first.
        for (int s = 0; s < 30; s++) { north += 3 * Kt; Step(h, ref own, 5, ac, 200, north, 3); }
        Assert.True(h.Said.All.Any(m => m.Contains("Delta A320", StringComparison.Ordinal)), All(h));
    }

    // ── The rules ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(300, 100, 10, 2.0, true)]       // 200 ft beyond its nearest, moving away
    [InlineData(299, 100, 10, 2.0, false)]      // not far enough
    [InlineData(600, 100, 0, 0.0, false)]       // stopped: it has not gone, however far
    [InlineData(600, 100, 10, 0.5, false)]      // the gap barely opens
    [InlineData(600, 100, 2.9, 2.0, false)]
    public void An_aircraft_has_passed_when_it_moves_away_200_feet_beyond_its_nearest(double gapFt, double minGapFt, double gsKts, double openingMps, bool passed)
        => Assert.Equal(passed, GroundTrafficLogic.HasPassed(gapFt, minGapFt, gsKts, openingMps));

    [Theory]
    [InlineData(10, 5, true)]       // a small stand: never less than 15 m
    [InlineData(16, 5, false)]
    [InlineData(30, 33, true)]
    [InlineData(41, 90, false)]     // never more than 40 m, whatever the stand's radius
    public void An_aircraft_is_on_a_stand_within_its_radius_clamped_to_15_and_40_metres(double distanceM, double radiusM, bool onStand)
        => Assert.Equal(onStand, GroundTrafficLogic.IsOnStand(distanceM, radiusM));

    [Fact]
    public void Only_taxi_guidance_not_being_engaged_yields_to_a_pushback_or_a_watched_aircraft()
    {
        Assert.True(GroundTrafficSuppression.IdleOnly(false, TaxiGuidanceState.Inactive));
        Assert.False(GroundTrafficSuppression.IdleOnly(true, TaxiGuidanceState.Inactive));        // the takeoff roll
        Assert.False(GroundTrafficSuppression.IdleOnly(false, TaxiGuidanceState.LandingRollout));
        Assert.False(GroundTrafficSuppression.IdleOnly(false, TaxiGuidanceState.Taxiing));
    }

    [Theory]
    [InlineData(TrafficCalloutKind.Warning, true)] [InlineData(TrafficCalloutKind.Caution, true)]
    [InlineData(TrafficCalloutKind.Clear, true)] [InlineData(TrafficCalloutKind.Converging, true)]
    [InlineData(TrafficCalloutKind.Watched, true)] [InlineData(TrafficCalloutKind.RunwayCritical, true)]
    [InlineData(TrafficCalloutKind.RunwayInfo, true)]
    [InlineData(TrafficCalloutKind.Awareness, false)] [InlineData(TrafficCalloutKind.OnRoute, false)]
    [InlineData(TrafficCalloutKind.QueueMoving, false)] [InlineData(TrafficCalloutKind.MoveUp, false)]
    [InlineData(TrafficCalloutKind.QueuePosition, false)]
    public void With_guidance_not_engaged_only_the_safety_lines_and_the_asked_aircraft_speak(TrafficCalloutKind kind, bool speaks)
        => Assert.Equal(speaks, TrafficSpeechPolicy.SpeaksWhileIdle(kind));
}
