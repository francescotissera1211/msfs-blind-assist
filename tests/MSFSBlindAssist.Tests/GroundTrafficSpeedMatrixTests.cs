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
    public void Closing_on_a_stopped_aircraft_Stop_comes_seven_seconds_before_a_100_ft_gap(double ownKts)
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
        double secondsToTouch = stopGapFt / fps, boundaryFt = 100 + 7 * fps;
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
}
