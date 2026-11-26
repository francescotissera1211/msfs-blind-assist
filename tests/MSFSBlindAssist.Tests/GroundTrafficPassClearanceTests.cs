// Passing BESIDE other aircraft: what is in the way and what is "very close" follow the clearance
// aerodromes are built to (GroundTrafficLogic.PassClearanceM, [TRF-17]), not a round distance. The
// situations here are the ordinary ones a controller never mentions (traffic on the parallel taxiway, the
// aircraft on the next stand, the one holding short of your taxiway) and the ones that are a real
// threat a few metres further in.
//
// Geometry: the equator, the own aircraft heading east along a route on the x axis; north is to its left.
// Sizes are the published ones: A320 37.57 x 35.80 m, A350-900 66.80 x 64.75 m, 777-300ER 73.86 x 64.80 m;
// an outline's nose is 46 % of its length ahead of its reported point.

using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;
using static MSFSBlindAssist.Tests.GroundTrafficHarness;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficPassClearanceTests
{
    private const double Kt = 0.514444;
    private static readonly double[] Speeds = { 5, 10, 15, 20, 25, 30 };

    public static IEnumerable<object[]> SpeedPairs()
        => from own in Speeds from traffic in Speeds select new object[] { own, traffic };

    private static AiTrafficDataEventArgs Traffic(uint id, double eastM, double northM, double gsKts, string type, double headingDeg)
    {
        var ac = Ac(id, eastM, northM, gsKts, "Delta", "DAL" + id, headingDeg);
        ac.AircraftType = type;
        return ac;
    }

    private static GroundTrafficHarness OnRoute(string ownType, double ownSpanFt)
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(6000), null, departure: false) };
        h.UseRealSizes(ownType, ownSpanFt);
        return h;
    }

    // The own aircraft rolls east at ownKts for the given seconds while the traffic is moved by `at`.
    private static List<string> Fly(GroundTrafficHarness h, double ownKts, int seconds, AiTrafficDataEventArgs ac,
        Func<int, (double East, double North, double Gs)>? at = null, double ownStartM = 0)
    {
        if (!h.Sim.Traffic.Contains(ac)) h.Sim.Traffic.Add(ac);
        double east = ownStartM;
        for (int s = 0; s < seconds; s++)
        {
            east += ownKts * Kt;
            if (at != null)
            {
                var (e, n, gs) = at(s + 1);
                ac.Longitude = e * M; ac.Latitude = n * M; ac.GroundSpeedKnots = gs;
            }
            h.Sim.Position = Own(east, Math.Abs(ownKts));
            h.Tick();
        }
        return h.Said.All.ToList();
    }

    private static bool IsAlarm(string m)
        => m.StartsWith("Stop", StringComparison.Ordinal) || m.StartsWith("Slow down", StringComparison.Ordinal)
           || m.Contains("converging", StringComparison.Ordinal);

    // ── The numbers ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(11.0, 11.0, 2.0)]      // two Cessnas (code A): two thirds of 3 m
    [InlineData(23.9, 10.0, 2.0)]
    [InlineData(24.0, 10.0, 3.0)]      // code C from 24 m: two thirds of 4.5 m
    [InlineData(35.8, 35.8, 3.0)]      // two A320s
    [InlineData(36.0, 20.0, 5.0)]      // code D and up: two thirds of 7.5 m
    [InlineData(11.0, 64.8, 5.0)]      // the LARGER of the two decides: a Cessna passing a 777
    [InlineData(64.75, 64.8, 5.0)]
    public void The_pass_clearance_is_two_thirds_of_the_ICAO_stand_clearance_for_the_larger_aircraft(double spanA, double spanB, double metres)
    {
        Assert.Equal(metres, GroundTrafficLogic.PassClearanceM(spanA, spanB));
        Assert.Equal(metres, GroundTrafficLogic.PassClearanceM(spanB, spanA));
    }

    [Theory]
    [InlineData(15, 0, 11.0, 5.0, true)]       // along the path, 11 m clear: the parallel taxiway
    [InlineData(15, 180, 11.0, 5.0, true)]     // the other way along it
    [InlineData(15, -160, 11.0, 5.0, true)]
    [InlineData(15, 20, 5.0, 5.0, true)]       // the limits
    [InlineData(15, 21, 11.0, 5.0, false)]     // not parallel: its track meets the path somewhere
    [InlineData(15, 90, 50.0, 5.0, false)]     // crossing
    [InlineData(15, 0, 4.9, 5.0, false)]       // parallel, but inside the clearance
    [InlineData(2.9, 0, 11.0, 5.0, false)]     // not moving: a stopped aircraft is judged by where it is
    [InlineData(15, 360, 11.0, 5.0, true)]
    public void Traffic_passes_clear_when_it_moves_along_the_path_and_stays_out_of_the_swept_strip(
        double gsKts, double offPathDeg, double lateralClearanceM, double passClearanceM, bool passesClear)
        => Assert.Equal(passesClear, GroundTrafficLogic.PassesClear(gsKts, offPathDeg, lateralClearanceM, passClearanceM));

    // A metre of latitude, exactly (the harness's M is a metre of longitude at the equator, 0.7 % more).
    private const double North = 1.0 / 110574.0;

    [Fact]
    public void The_route_clearance_is_measured_from_the_outline_to_the_strip_the_own_wings_sweep()
    {
        var route = East(1000);
        var b77w = AircraftHull.FromSize(GroundTrafficAircraftSizes.Resolve("B77W", null, null, 0));
        var a320 = AircraftHull.FromSize(GroundTrafficAircraftSizes.Resolve("A320", null, null, 0));
        const double ownHalfSpan = 32.375;                             // an A350-900

        // A 777 on the parallel taxiway 76 m to the side, heading along it: wingtip to wingtip.
        Assert.Equal(76 - b77w.HalfSpanM - ownHalfSpan, GroundTrafficLogic.RouteClearanceM(route, b77w, 90, -76 * North, 400 * M, 400, ownHalfSpan), 1);
        // An A320 holding short of the route, nose toward it and 36 m from its line.
        Assert.Equal(36 - ownHalfSpan, GroundTrafficLogic.RouteClearanceM(route, a320, 0, -(36 + 17.28) * North, 400 * M, 400, ownHalfSpan), 1);
        // The same aircraft parked tail toward the route: its tail is 54 % of its length behind its point.
        Assert.Equal(40 - ownHalfSpan, GroundTrafficLogic.RouteClearanceM(route, a320, 180, -(40 + 20.29) * North, 400 * M, 400, ownHalfSpan), 1);
        // On the route: inside the strip.
        Assert.True(GroundTrafficLogic.RouteClearanceM(route, a320, 90, 0, 400 * M, 400, ownHalfSpan) < 0);
        // No route to measure against.
        Assert.Equal(double.MaxValue, GroundTrafficLogic.RouteClearanceM(new List<GroundTrafficRoutePoint>(), a320, 90, 0, 0, 0, ownHalfSpan));
    }

    [Fact]
    public void The_route_clearance_follows_a_bend()
    {
        // The route runs east 300 m then north. An A320 parked east of the bend, tail toward the northbound
        // leg and 25 m from it, is 25 m from the route though 100 m and more from the eastbound leg's line.
        var route = new List<GroundTrafficRoutePoint> { new(0, 0, "A", 0), new(0, 300 * M, "A", 300), new(400 * North, 300 * M, "B", 700) };
        var a320 = AircraftHull.FromSize(GroundTrafficAircraftSizes.Resolve("A320", null, null, 0));
        double clearance = GroundTrafficLogic.RouteClearanceM(route, a320, 90, 150 * North, (300 + 25 + 20.29) * M, 450, 17.9);
        Assert.Equal(25 - 17.9, clearance, 1);
    }

    // ── The parallel taxiway ─────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SpeedPairs))]
    public void A_widebody_going_the_other_way_on_the_parallel_taxiway_is_no_alarm(double ownKts, double trafficKts)
    {
        // Code E taxiways 76 m apart, an A350 and a 777: 11 m between the wingtips as they pass. Until
        // 2026-11-26: "converging from ahead", "Slow down", "Stop ... very close, 450 feet".
        var h = OnRoute("A359", 212.4);
        var said = Fly(h, ownKts, 110, Traffic(1, 1500, -76, trafficKts, "B77W", 270), s => (1500 - s * trafficKts * Kt, -76, trafficKts));
        Assert.DoesNotContain(said, IsAlarm);
        // It is pointed out, as what it is: never "head-on", which is traffic coming AT the pilot.
        Assert.DoesNotContain(said, m => m.Contains("head-on", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SpeedPairs))]
    public void Overtaking_or_being_overtaken_on_the_parallel_taxiway_is_no_alarm(double ownKts, double trafficKts)
    {
        var h = OnRoute("A359", 212.4);
        var said = Fly(h, ownKts, 110, Traffic(1, 300, -76, trafficKts, "B77W", 90), s => (300 + s * trafficKts * Kt, -76, trafficKts));
        Assert.DoesNotContain(said, IsAlarm);
    }

    [Theory]
    [InlineData("A359", 212.4, "B77W", 76.0)]      // code E, the least their taxiways are apart
    [InlineData("A320", 117.5, "A320", 44.0)]      // code C: 8 m between the wingtips
    [InlineData("A320", 117.5, "B77W", 60.0)]
    public void Passing_an_aircraft_stopped_on_the_parallel_taxiway_is_no_alarm(string own, double ownSpanFt, string type, double apartM)
    {
        // Until 2026-11-26 the A350 passing the 777 was "Stop, Delta 777 very close, to the right, 50 feet",
        // and the A320 on a parallel 44 m away was "in the way": "Slow down", then "Stop" at 250 feet.
        var h = OnRoute(own, ownSpanFt);
        var said = Fly(h, 15, 110, Traffic(1, 500, -apartM, 0, type, 90));
        Assert.DoesNotContain(said, IsAlarm);
        Assert.Contains(said, m => m.Contains("stopped", StringComparison.Ordinal));          // it is still pointed out
    }

    [Theory]
    [MemberData(nameof(SpeedPairs))]
    public void Two_A320s_meeting_on_code_C_parallels_is_no_alarm(double ownKts, double trafficKts)
    {
        var h = OnRoute("A320", 117.5);
        var said = Fly(h, ownKts, 110, Traffic(1, 1500, -44, trafficKts, "A320", 270), s => (1500 - s * trafficKts * Kt, -44, trafficKts));
        Assert.DoesNotContain(said, IsAlarm);
    }

    [Fact]
    public void Traffic_on_a_track_that_is_parallel_but_too_close_is_still_an_alarm()
    {
        // The same meeting 38 m apart: 2.2 m between the wingtips. Not a pass anyone is built for.
        var h = OnRoute("A320", 117.5);
        var said = Fly(h, 10, 170, Traffic(1, 1500, -38, 10, "A320", 270), s => (1500 - s * 10 * Kt, -38, 10));
        Assert.True(said.Any(m => m.StartsWith("Stop", StringComparison.Ordinal)), string.Join(" | ", h.Transcript));
    }

    // ── Holding short of your taxiway, parked beside it ──────────────────────────────────────────

    [Theory]
    [InlineData(25.0, true)]       // its nose inside the strip an A350's wing sweeps (32.4 m)
    [InlineData(36.0, true)]       // 3.6 m outside it: under the 5 m aircraft are allowed to pass with
    [InlineData(38.0, false)]      // 5.6 m: clear
    [InlineData(50.0, false)]
    public void An_aircraft_holding_short_of_your_taxiway_is_in_the_way_only_when_its_nose_is(double noseFromLineM, bool inTheWay)
    {
        // "Give way to" and "pass behind" are the controller's; whether the nose of the one holding on the
        // crossing taxiway leaves room for the wing is the pilot's. An A320 nose-on to the route.
        var h = OnRoute("A359", 212.4);
        var said = Fly(h, 12, 70, Traffic(1, 400, -(noseFromLineM + 17.28), 0, "A320", 0));
        if (inTheWay)
        {
            Assert.Contains(said, m => m.StartsWith("Slow down, Delta A320", StringComparison.Ordinal));
            Assert.Contains(said, m => m.StartsWith("Stop, Delta A320 very close", StringComparison.Ordinal));
        }
        else Assert.DoesNotContain(said, IsAlarm);
    }

    [Theory]
    [InlineData(40.0, false)]      // a stand taxilane built to the standard: 7.6 m behind the tails
    [InlineData(38.0, false)]      // 5.6 m
    [InlineData(35.0, true)]       // 2.6 m
    public void A_row_of_parked_aircraft_is_an_alarm_only_when_a_tail_is_in_the_wings_way(double tailFromLineM, bool inTheWay)
    {
        // An A350 taxies along a stand taxilane behind five 777s parked nose-in, tails toward the taxilane.
        var h = OnRoute("A359", 212.4);
        for (uint i = 0; i < 5; i++)
            h.Sim.Traffic.Add(Traffic(i + 1, 300 + i * 80, -(tailFromLineM + 39.88), 0, "B77W", 180));
        var said = Fly(h, 10, 150, h.Sim.Traffic[0]);
        Assert.Equal(inTheWay, said.Any(m => m.StartsWith("Stop", StringComparison.Ordinal)));
        if (!inTheWay) Assert.DoesNotContain(said, IsAlarm);
    }

    // ── Pushed back ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(7.5)]              // the next stand at the standard clearance
    [InlineData(5.5)]
    [InlineData(3.0)]              // even this near: pushed straight back, the gap only opens
    public void Pushed_back_past_the_aircraft_on_the_next_stand_is_no_alarm(double wingtipGapM)
    {
        // A 777 is pushed straight back at 3 kt; another 777 is parked parallel on the next stand. Until
        // 2026-11-26 every pushback beside a neighbour within 50 ft plus the lead was "Stop".
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.UseRealSizes("B77W", 212.6);
        h.Sim.Position = Own(0, 0);
        var b77w = AircraftHull.FromSize(GroundTrafficAircraftSizes.Resolve("B77W", null, null, 0));
        var said = Fly(h, -3, 45, Traffic(1, 0, 2 * b77w.HalfSpanM + wingtipGapM, 0, "B77W", 90));
        Assert.DoesNotContain(said, IsAlarm);
    }

    [Fact]
    public void Pushed_back_toward_an_aircraft_the_wingtip_would_reach_is_Stop()
    {
        // Parked behind and to the side: not behind the tail, but the left wingtip passes 2 m from its nose.
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.UseRealSizes("B77W", 212.6);
        h.Sim.Position = Own(0, 0);
        var b77w = AircraftHull.FromSize(GroundTrafficAircraftSizes.Resolve("B77W", null, null, 0));
        var said = Fly(h, -3, 60, Traffic(1, -120, b77w.HalfSpanM + 2.0 + 17.28, 0, "A320", 180));
        Assert.Contains(said, m => m.StartsWith("Stop, Delta A320 very close", StringComparison.Ordinal));
    }

    // ── From behind ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_aircraft_taxiing_up_behind_a_stopped_pilot_is_pointed_out_and_nothing_more()
    {
        // Holding in a line: the next aircraft taxies up at 15 kt and stops 40 m behind. Keeping its
        // distance is its pilot's job; "Stop" would be no use to a pilot who is stopped.
        var h = OnRoute("A320", 117.5);
        var said = Fly(h, 0, 70, Traffic(1, -500, 0, 15, "A320", 90),
            s => { double x = Math.Min(-500 + s * 15 * Kt, -77.6); return (x, 0, x < -77.6 ? 15 : 0); });
        Assert.DoesNotContain(said, IsAlarm);
        Assert.Contains(said, m => m.Contains("behind", StringComparison.Ordinal));
    }

    [Fact]
    public void A_faster_aircraft_catching_up_from_behind_is_no_alarm()
    {
        var h = OnRoute("A320", 117.5);
        var said = Fly(h, 15, 80, Traffic(1, -400, 0, 25, "A320", 90),
            s => { double own = s * 15 * Kt, x = Math.Min(-400 + s * 25 * Kt, own - 97.6); return (x, 0, x < own - 97.6 ? 25 : 15); });
        Assert.DoesNotContain(said, IsAlarm);
    }

    // ── Traffic that does come into the path ─────────────────────────────────────────────────────

    [Fact]
    public void An_aircraft_pushed_back_onto_the_route_ahead_is_Stop()
    {
        // 330 m ahead an A320 is pushed back from its stand, tail-first, onto the route and stops there.
        var h = OnRoute("A320", 117.5);
        var said = Fly(h, 10, 70, Traffic(1, 330, -70, 3, "A320", 180),
            s => { double n = Math.Min(-70 + s * 3 * Kt, 0); return (330, n, n < 0 ? 3 : 0); });
        Assert.Contains(said, m => m.StartsWith("Slow down, Delta A320 ahead", StringComparison.Ordinal));
        Assert.Contains(said, m => m.StartsWith("Stop, Delta A320 very close, ahead", StringComparison.Ordinal));
    }
}
