using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// MSFSBA answers in the units the pilot set on the G1000.
///
/// The settings are Working Title user settings held inside the instrument — not SimVars,
/// not L:vars — so they reach the app as a row on the display scrape and nowhere else.
/// That makes the ROW WORDING a contract between the agent and this class, and the first
/// thing worth pinning.
/// </summary>
public class CowsDA40DisplayUnitsTests
{
    private static CowsDA40Definition Ng() => new(DA40Variant.NG);

    private static string Render(CowsDA40Definition def, string varKey, double value)
    {
        Assert.True(def.TryGetDisplayOverride(varKey, value, out string text),
            varKey + " has no display override at all.");
        return text;
    }

    [Fact]
    public void DefaultsAreTheG1000sOwnFactoryDefaults()
    {
        // A pilot who has changed nothing must hear exactly what they heard before, and
        // these are what the aeroplane loads with.
        var def = Ng();

        Assert.Equal("nautical", def.DisplayUnitDistance);
        Assert.Equal("feet", def.DisplayUnitAltitude);
        Assert.Equal("celsius", def.DisplayUnitTemperature);
        Assert.Equal("pounds", def.DisplayUnitWeight);
        Assert.Equal("gallons", def.DisplayUnitFuel);
        Assert.Equal("magnetic", def.DisplayUnitBearings);
    }

    [Fact]
    public void TheAgentsRowIsUnderstood()
    {
        var def = Ng();

        def.NoteDisplayUnits(new List<string>
        {
            "Page: Aux – System Setup 1",
            "Display units: bearings true, distance metric, altitude meters, " +
            "temperature fahrenheit, weight kilograms, fuel liters",
            "Softkey 1: Engine"
        });

        Assert.Equal("true", def.DisplayUnitBearings);
        Assert.Equal("metric", def.DisplayUnitDistance);
        Assert.Equal("meters", def.DisplayUnitAltitude);
        Assert.Equal("fahrenheit", def.DisplayUnitTemperature);
        Assert.Equal("kilograms", def.DisplayUnitWeight);
        Assert.Equal("liters", def.DisplayUnitFuel);
    }

    [Fact]
    public void ATwoWordUnitSurvivesTheSplit()
    {
        // "imperial gallons" is one value in two words. Reading only the token after the
        // dimension would leave the fuel unit as "imperial", which matches nothing.
        var def = Ng();
        def.NoteDisplayUnits(new[] { "Display units: fuel imperial gallons" });

        Assert.Equal("imperial gallons", def.DisplayUnitFuel);
    }

    [Fact]
    public void AScrapeWithNoUnitsRowChangesNothing()
    {
        var def = Ng();
        def.NoteDisplayUnits(new[] { "CAS messages: 0", "Autopilot: off" });

        Assert.Equal("pounds", def.DisplayUnitWeight);
        Assert.Equal("celsius", def.DisplayUnitTemperature);
    }

    [Fact]
    public void WeightFollowsTheG1000()
    {
        var def = Ng();

        // Baggage is a plain pounds readout with a limit beside it, so it is the one that
        // shows the conversion without a band in the way.
        string before = Render(def, "DA40_GROSS_WEIGHT", 2000);
        Assert.Contains("pounds", before, StringComparison.Ordinal);

        def.NoteDisplayUnits(new[] { "Display units: weight kilograms" });
        Assert.Equal("kilograms", def.DisplayUnitWeight);
    }

    [Fact]
    public void TemperatureFollowsTheG1000()
    {
        var def = Ng();

        string celsius = Render(def, "DA40_ELEC_BATT_TEMP", 20);
        Assert.Contains("20", celsius, StringComparison.Ordinal);
        Assert.Contains("celsius", celsius, StringComparison.Ordinal);

        def.NoteDisplayUnits(new[] { "Display units: temperature fahrenheit" });

        string fahrenheit = Render(def, "DA40_ELEC_BATT_TEMP", 20);
        Assert.Contains("68", fahrenheit, StringComparison.Ordinal);
        Assert.Contains("fahrenheit", fahrenheit, StringComparison.Ordinal);
        Assert.DoesNotContain("celsius", fahrenheit, StringComparison.Ordinal);
    }

    [Fact]
    public void AGaugesBandIsReadFromTheRawValue()
    {
        // ⚠️ The arc is a PHYSICAL fact about the engine — the green on the oil-temperature
        // gauge is the same span of heat in either scale — so switching to fahrenheit must
        // change the NUMBER and not which band the needle is in.
        var def = Ng();

        string celsius = Render(def, "DA40_START_OIL_TEMP", 90);
        def.NoteDisplayUnits(new[] { "Display units: temperature fahrenheit" });
        string fahrenheit = Render(def, "DA40_START_OIL_TEMP", 90);

        Assert.Contains("194", fahrenheit, StringComparison.Ordinal);
        Assert.Contains("fahrenheit", fahrenheit, StringComparison.Ordinal);

        // Whatever the band words are, they must be the same in both.
        string bandOf(string s) => string.Join(" ",
            s.Split(',').Skip(1).Select(part => part.Trim()));

        Assert.Equal(bandOf(celsius), bandOf(fahrenheit));
    }

    [Fact]
    public void FuelFollowsTheG1000AndKeepsItsIndicationLimit()
    {
        var def = Ng();

        string gallons = Render(def, "DA40_FUEL_MAIN_IND", 10);
        Assert.Contains("gallons", gallons, StringComparison.Ordinal);

        def.NoteDisplayUnits(new[] { "Display units: fuel liters" });
        string litres = Render(def, "DA40_FUEL_MAIN_IND", 10);

        Assert.Contains("37.9", litres, StringComparison.Ordinal);
        Assert.Contains("litres", litres, StringComparison.Ordinal);

        // The AFM's "at the indication limit" warning is about a quantity of fuel and must
        // survive the change of units.
        def.NoteDisplayUnits(new[] { "Display units: fuel gallons" });
        Assert.Contains("indication limit", Render(def, "DA40_FUEL_MAIN_IND", 14),
            StringComparison.Ordinal);
        def.NoteDisplayUnits(new[] { "Display units: fuel liters" });
        Assert.Contains("indication limit", Render(def, "DA40_FUEL_MAIN_IND", 14),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFuelUnitWeCannotConvertIsLeftAloneRatherThanGuessed()
    {
        // The Garmin also offers weight-based fuel. Converting gallons to pounds needs the
        // fuel's density; inventing one would put a wrong number in front of a pilot
        // planning a flight on it.
        var def = Ng();
        def.NoteDisplayUnits(new[] { "Display units: fuel pounds" });

        string text = Render(def, "DA40_FUEL_MAIN_IND", 10);
        Assert.Contains("10.0 gallons", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BearingsAreReadButNeverConverted()
    {
        // Magnetic to true is a change of DATUM, not of units, and the heading bug and
        // course are WRITTEN in magnetic — showing a true bearing in a field the pilot
        // types into would send the aeroplane somewhere else.
        var def = Ng();
        def.NoteDisplayUnits(new[] { "Display units: bearings true" });

        Assert.Equal("true", def.DisplayUnitBearings);

        string heading = Render(def, "DA40_STBY_COMPASS", 100);
        Assert.Contains("100", heading, StringComparison.Ordinal);
        Assert.DoesNotContain("true", heading, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AUnitTheG1000HasNoSettingForIsUntouched()
    {
        var def = Ng();
        def.NoteDisplayUnits(new[]
        {
            "Display units: temperature fahrenheit, weight kilograms, fuel liters"
        });

        // Volts are volts.
        Assert.Contains("volts", Render(def, "DA40_ELEC_BUS_MAIN_VOLT", 28), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCasBlockEndsWhereTheCasBlockEnds()
    {
        // The field list every page now carries is indented exactly like a CAS message, so
        // an indentation-only rule announced "Weight: Pounds" as a caution.
        var messages = CowsDA40Definition.ExtractCasMessages(new[]
        {
            "CAS messages: 1",
            "  Caution: PITOT HT OFF",
            "Autopilot: off",
            "Fields (3), cursor on:",
            "  Time Format: UTC",
            "  Weight: Pounds(LB)",
            "Timer and References:",
            "  Timer 0:00:00",
            "Softkey 1: Engine"
        });

        Assert.Equal(new[] { "Caution: PITOT HT OFF" }, messages.ToArray());
    }

    [Fact]
    public void TheAgentEmitsTheRowThisClassReads()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources",
            "coherent-da40-g1000-agent.js"));

        // The prefix and every dimension word, because a rename on either side is silent.
        Assert.Contains("\"Display units: \"", agent, StringComparison.Ordinal);
        foreach (string dimension in new[]
        {
            "bearings", "distance", "altitude", "temperature", "weight", "fuel"
        })
        {
            Assert.Contains("\"" + dimension + "\"", agent, StringComparison.Ordinal);
        }

        // And it must be pushed onto BOTH displays: the always-on CAS monitor reads the
        // PFD, and the MFD is where the pilot changes the setting.
        Assert.Equal(2, agent.Split("A.pushUnits(rows)").Length - 1);
    }
}
