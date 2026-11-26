// The traffic callouts at every taxi speed from 5 to 30 kt, own aircraft and traffic alike: when "Slow
// down" and "Stop" come against the gap and the time left, and how close "converging, about N seconds"
// is to the seconds that are really left. Each test prints its table (dotnet test --logger
// "console;verbosity=detailed").

using MSFSBlindAssist.Services;
using Xunit;
using Xunit.Abstractions;
using static MSFSBlindAssist.Tests.GroundTrafficHarness;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficSpeedMatrixTests
{
    private readonly ITestOutputHelper _out;
    public GroundTrafficSpeedMatrixTests(ITestOutputHelper output) => _out = output;

    private const double Kt = 0.514444;                      // metres per second per knot
    private static readonly double[] Speeds = { 5, 10, 15, 20, 25, 30 };

    public static IEnumerable<object[]> OwnSpeeds() => Speeds.Select(s => new object[] { s });

    public static IEnumerable<object[]> SpeedPairs()
        => from own in Speeds from traffic in Speeds select new object[] { own, traffic };

    [Theory]
    [MemberData(nameof(OwnSpeeds))]
    public void Closing_on_a_stopped_aircraft_Stop_comes_its_lead_before_a_100_ft_gap(double ownKts)
    {
        // An A320 stopped on the route 900 m ahead of an A320: nose 17.3 m ahead of the own point, its
        // tail 20.3 m behind its own, so the gap is the distance between the two points less 37.6 m.
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        var ahead = Ac(1, 900, 0, 0, "Delta", "DAL1");
        h.Sim.Traffic.Add(ahead);
        double east = 0, slowGapFt = double.NaN, stopGapFt = double.NaN;
        for (int s = 0; s < 400 && double.IsNaN(stopGapFt); s++)
        {
            east += ownKts * Kt;
            h.Sim.Position = Own(east, ownKts);
            int before = h.Said.All.Count;
            h.Tick();
            double gapFt = (900 - east - 37.6) * GroundTrafficLogic.FeetPerMetre;
            foreach (string m in h.Said.All.Skip(before))
            {
                if (m.StartsWith("Slow down", StringComparison.Ordinal) && double.IsNaN(slowGapFt)) slowGapFt = gapFt;
                if (m.StartsWith("Stop", StringComparison.Ordinal)) stopGapFt = gapFt;
            }
        }
        double fps = ownKts * 1.6878;
        double secondsToTouch = stopGapFt / fps, boundaryFt = 100 + GroundTrafficLogic.ZoneLeadFt(ownKts, 7.0);
        _out.WriteLine(FormattableString.Invariant(
            $"own {ownKts,2} kt: Slow down at {slowGapFt,4:0} ft ({slowGapFt * 0.3048,3:0} m), Stop at {stopGapFt,4:0} ft ({stopGapFt * 0.3048,3:0} m) = {secondsToTouch:0.0} s from touching; rule {boundaryFt:0} ft"));

        Assert.False(double.IsNaN(stopGapFt), string.Join(" | ", h.Transcript));
        Assert.False(double.IsNaN(slowGapFt), string.Join(" | ", h.Transcript));
        Assert.True(slowGapFt > stopGapFt);
        // No later than two seconds of travel past the boundary (a tick, and the sweep it waits for), never early.
        Assert.InRange(stopGapFt, boundaryFt - 2.0 * fps - 1.0, boundaryFt + 1.0);
        // Which always leaves five seconds and more before the two would touch.
        Assert.True(secondsToTouch >= 5.0, $"{secondsToTouch:0.0} s");
    }

    [Theory]
    [MemberData(nameof(OwnSpeeds))]
    public void A_pilot_who_takes_two_seconds_and_brakes_gently_stops_short(double ownKts)
    {
        // The pilot hears "Stop", keeps rolling for two seconds, then brakes at 1 m/s2. With a flat seven
        // seconds of lead this pilot hit the aircraft ahead from 30 kt and stopped 10 m short from 25.
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        h.Sim.Traffic.Add(Ac(1, 1200, 0, 0, "Delta", "DAL1"));
        double east = 0, mps = ownKts * Kt; int reacting = -1;
        for (int s = 0; s < 600 && mps > 0; s++)
        {
            if (reacting < 0 && h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal))) reacting = 0;
            if (reacting >= 0 && ++reacting > 2) mps = Math.Max(0.0, mps - GroundTrafficLogic.GentleBrakingMps2);
            east += mps;
            h.Sim.Position = Own(east, mps / Kt);
            h.Tick();
        }
        double gapM = 1200 - east - 37.6;
        _out.WriteLine(FormattableString.Invariant($"own {ownKts,2} kt: stopped with {gapM:0} m of clear space"));
        Assert.True(reacting >= 0, "no Stop: " + string.Join(" | ", h.Transcript));
        Assert.True(gapM >= 20.0, FormattableString.Invariant($"{gapM:0.0} m"));
    }

    [Theory]
    [MemberData(nameof(SpeedPairs))]
    public void Converging_names_the_seconds_that_are_left(double ownKts, double trafficKts)
    {
        // The own aircraft rolls east, the traffic north across its path, both due at the crossing
        // 45 seconds from the start: a collision course at every pair of speeds.
        const double due = 45.0;
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        double crossingEast = ownKts * Kt * due, startNorth = -trafficKts * Kt * due;
        var crosser = Ac(1, crossingEast, startNorth, trafficKts, "Delta", "DAL1", headingDeg: 0);
        h.Sim.Traffic.Add(crosser);
        double east = 0, north = startNorth;
        string? spoken = null; double spokenAt = double.NaN, firstStopLeft = double.NaN;
        for (int s = 1; s <= 44 && double.IsNaN(firstStopLeft); s++)
        {
            east += ownKts * Kt; north += trafficKts * Kt;
            crosser.Latitude = north * M;
            h.Sim.Position = Own(east, ownKts);
            int before = h.Said.All.Count;
            h.Tick();
            foreach (string m in h.Said.All.Skip(before))
            {
                if (m.Contains("converging", StringComparison.Ordinal) && spoken == null) { spoken = m; spokenAt = s; }
                if (m.StartsWith("Stop", StringComparison.Ordinal)) firstStopLeft = due - s;
            }
        }
        string said = string.Join(" | ", h.Transcript);
        Assert.True(spoken != null || !double.IsNaN(firstStopLeft), said);
        if (spoken != null)
        {
            int seconds = int.Parse(System.Text.RegularExpressions.Regex.Match(spoken, @"about (\d+) seconds").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            double left = due - spokenAt;
            _out.WriteLine(FormattableString.Invariant(
                $"own {ownKts,2} kt, traffic {trafficKts,2} kt: \"about {seconds} seconds\" with {left:0} s left; Stop with {firstStopLeft:0} s left"));
            // Spoken to the nearest 5 s, from a sample up to a second old: within 6 s of the truth.
            Assert.InRange(seconds, left - 6.0, left + 6.0);
        }
        else
            _out.WriteLine(FormattableString.Invariant($"own {ownKts,2} kt, traffic {trafficKts,2} kt: no converging call; Stop with {firstStopLeft:0} s left"));
    }

    [Theory]
    [MemberData(nameof(SpeedPairs))]
    public void Following_slower_traffic_Stop_still_leaves_time(double ownKts, double trafficKts)
    {
        if (trafficKts >= ownKts) return;                     // nothing closes
        // Traffic taxiing the same way on the route ahead, slower than the pilot who is catching it up.
        var h = new GroundTrafficHarness { Context = RouteContext(East(6000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        double lead = 600, east = 0;
        var ahead = Ac(1, lead, 0, trafficKts, "Delta", "DAL1");
        h.Sim.Traffic.Add(ahead);
        double stopGapM = double.NaN;
        for (int s = 0; s < 600 && double.IsNaN(stopGapM); s++)
        {
            east += ownKts * Kt; lead += trafficKts * Kt;
            ahead.Longitude = lead * M;
            h.Sim.Position = Own(east, ownKts);
            int before = h.Said.All.Count;
            h.Tick();
            if (h.Said.All.Skip(before).Any(m => m.StartsWith("Stop", StringComparison.Ordinal))) stopGapM = lead - east - 37.6;
            if (lead - east - 37.6 < 0) break;
        }
        double closingMps = (ownKts - trafficKts) * Kt;
        Assert.False(double.IsNaN(stopGapM), "touched with no Stop: " + string.Join(" | ", h.Transcript));
        _out.WriteLine(FormattableString.Invariant(
            $"own {ownKts,2} kt behind {trafficKts,2} kt: Stop at {stopGapM,3:0} m = {stopGapM / closingMps:0} s from touching at the closing speed"));
        Assert.True(stopGapM / closingMps >= 5.0, $"{stopGapM / closingMps:0.0} s");
    }

    [Theory]
    [MemberData(nameof(SpeedPairs))]
    public void Head_on_traffic_Stop_leaves_the_time_a_stopped_aircraft_does(double ownKts, double trafficKts)
    {
        // Two A320s nose to nose on the route, each rolling toward the other: the gap is the distance
        // between the two points less both noses (17.3 m each). The gap closes at BOTH speeds, so "Stop"
        // given for the pilot's own speed alone left 3.5 s at 10 kt against 10 kt, where a stopped aircraft
        // ahead leaves 7.
        var h = new GroundTrafficHarness { Context = RouteContext(East(6000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        double its = 2500, east = 0;
        var headOn = Ac(1, its, 0, trafficKts, "Delta", "DAL1", headingDeg: 270);
        h.Sim.Traffic.Add(headOn);
        double stopGapM = double.NaN;
        for (int s = 0; s < 600 && double.IsNaN(stopGapM); s++)
        {
            east += ownKts * Kt; its -= trafficKts * Kt;
            headOn.Longitude = its * M;
            h.Sim.Position = Own(east, ownKts);
            int before = h.Said.All.Count;
            h.Tick();
            if (h.Said.All.Skip(before).Any(m => m.StartsWith("Stop", StringComparison.Ordinal))) stopGapM = its - east - 34.6;
            if (its - east - 34.6 < 0) break;
        }
        double closingMps = (ownKts + trafficKts) * Kt;
        Assert.False(double.IsNaN(stopGapM), "touched with no Stop: " + string.Join(" | ", h.Transcript));
        _out.WriteLine(FormattableString.Invariant(
            $"own {ownKts,2} kt against {trafficKts,2} kt: Stop at {stopGapM,3:0} m = {stopGapM / closingMps:0.0} s from touching at the closing speed"));
        Assert.True(stopGapM / closingMps >= 5.0, $"{stopGapM / closingMps:0.0} s");
    }

    [Theory]
    [MemberData(nameof(SpeedPairs))]
    public void Traffic_going_the_other_way_on_the_parallel_taxiway_is_never_Stop(double ownKts, double trafficKts)
    {
        // The same two A320s, the other one on a taxiway 80 m to the side (44 m between the wingtips as they
        // pass). It closes as fast as head-on traffic does and never comes near: no lead for it, no "Slow
        // down", no "Stop".
        var h = new GroundTrafficHarness { Context = RouteContext(East(6000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        double its = 1500, east = 0;
        var other = Ac(1, its, 80, trafficKts, "Delta", "DAL1", headingDeg: 270);
        h.Sim.Traffic.Add(other);
        for (int s = 0; s < 600 && its > east - 300; s++)
        {
            east += ownKts * Kt; its -= trafficKts * Kt;
            other.Longitude = its * M;
            h.Sim.Position = Own(east, ownKts);
            h.Tick();
        }
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Stop", StringComparison.Ordinal)
            || m.StartsWith("Slow down", StringComparison.Ordinal));
    }

    // ── Following ("follow the 737 to the holding point") ────────────────────────────────────────

    [Theory]
    [MemberData(nameof(OwnSpeeds))]
    public void Following_at_the_same_speed_is_Stop_only_inside_the_following_distance(double kts)
    {
        // Behind an aircraft taxiing at the pilot's own speed the room needed is 100 ft plus the three
        // seconds it takes to react: if it brakes, it rolls as far braking as the pilot will. Until
        // 2026-11-26 the lead was that for a PARKED aircraft, and following at 80 m and 15 kt was "Stop".
        double followingM = 30.48 + GroundTrafficLogic.StopReactionSec * kts * Kt;
        Assert.DoesNotContain(Follow(kts, followingM + 10, 60), m => m.StartsWith("Stop", StringComparison.Ordinal));
        Assert.Contains(Follow(kts, followingM - 10, 60), m => m.StartsWith("Stop", StringComparison.Ordinal));
    }

    private static List<string> Follow(double kts, double gapM, int seconds)
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(8000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        double lead = gapM + 37.6, east = 0;
        var ahead = Ac(1, lead, 0, kts, "Delta", "DAL1");
        h.Sim.Traffic.Add(ahead);
        for (int s = 0; s < seconds; s++)
        {
            east += kts * Kt; lead += kts * Kt;
            ahead.Longitude = lead * M;
            h.Sim.Position = Own(east, kts);
            h.Tick();
        }
        return h.Said.All.ToList();
    }

    [Theory]
    [MemberData(nameof(OwnSpeeds))]
    public void The_aircraft_being_followed_brakes_to_a_stop_and_the_pilot_stops_short_of_it(double kts)
    {
        // The pilot follows just outside the following distance. The aircraft ahead brakes to a stop at
        // 1 m/s2; the pilot hears "Stop", takes two seconds, and brakes as gently.
        var h = new GroundTrafficHarness { Context = RouteContext(East(8000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        double followingM = 30.48 + GroundTrafficLogic.StopReactionSec * kts * Kt + 5;
        double lead = followingM + 37.6, east = 0, its = kts * Kt, mps = kts * Kt;
        var ahead = Ac(1, lead, 0, kts, "Delta", "DAL1");
        h.Sim.Traffic.Add(ahead);
        int reacting = -1;
        for (int s = 0; s < 200 && (mps > 0 || its > 0); s++)
        {
            if (s >= 20) its = Math.Max(0.0, its - GroundTrafficLogic.GentleBrakingMps2);
            if (reacting < 0 && h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal))) reacting = 0;
            if (reacting >= 0 && ++reacting > 2) mps = Math.Max(0.0, mps - GroundTrafficLogic.GentleBrakingMps2);
            east += mps; lead += its;
            ahead.Longitude = lead * M; ahead.GroundSpeedKnots = its / Kt;
            h.Sim.Position = Own(east, mps / Kt);
            h.Tick();
        }
        double gapM = lead - east - 37.6;
        _out.WriteLine(FormattableString.Invariant($"both {kts,2} kt, following at {followingM:0} m: stopped {gapM:0.0} m behind it"));
        Assert.True(mps == 0, "never told to stop: " + string.Join(" | ", h.Transcript));
        Assert.True(gapM >= 15.0, FormattableString.Invariant($"{gapM:0.0} m"));
    }

    [Theory]
    [InlineData(15, 0, 54.0)]          // a parked aircraft: seven seconds of travel
    [InlineData(15, 15, 23.1)]         // followed at its own speed: the reaction time alone
    [InlineData(15, 12, 33.9)]         // catching it up slowly
    [InlineData(15, 3, 51.7)]          // it has only just moved off: nearly a parked one
    [InlineData(15, 25, 23.1)]         // it is faster: never less than the reaction time
    [InlineData(30, 0, 165.4)] [InlineData(30, 30, 46.3)]
    [InlineData(0, 10, 0.0)]
    public void The_lead_against_traffic_moving_away_is_the_following_distance(double ownKts, double recedingKts, double metres)
        => Assert.Equal(metres, GroundTrafficLogic.ZoneLeadFt(ownKts, 7.0, recedingKts) / GroundTrafficLogic.FeetPerMetre, 1);
}
