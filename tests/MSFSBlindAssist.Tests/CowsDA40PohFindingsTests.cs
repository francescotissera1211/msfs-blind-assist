using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The things the COWS POH says that MSFSBA could not say back.
///
/// Each of these is a fact stated in the aircraft's own manual, verified against the
/// model's own XML, and previously unreachable from MSFSBA — either because no variable
/// was bound or because the variant that has it was being handed the other variant's
/// list. What is pinned here is the CONCLUSION of each, because the manual and the model
/// are both outside the repository and neither can be read from CI.
/// </summary>
public class CowsDA40PohFindingsTests
{
    private static CowsDA40Definition Xls() => new(DA40Variant.XLS);
    private static CowsDA40Definition Ng() => new(DA40Variant.NG);

    // ------------------------------------------------------------------ Automixture (XLS)

    /// <summary>
    /// ⚠️ AUTOMIXTURE IS A REGIME CHANGE THE PILOT CANNOT SEE. It is not a cockpit switch:
    /// the model DETECTS the simulator's own mixture assistance and ramps `L:AUTOMIXTURE`
    /// to 1. With it set the lever picks an air/fuel target instead of driving the valve,
    /// and every cylinder's charge is clamped at 2 g — so the engine cannot be flooded and
    /// the whole hot-start problem the Priming panel exists for cannot arise.
    /// </summary>
    [Fact]
    public void TheXlsReportsWhetherAutomixtureIsInPlay()
    {
        var vars = Xls().GetVariables();
        Assert.True(vars.ContainsKey("DA40_XLS_AUTOMIXTURE"));
        Assert.Equal("AUTOMIXTURE", vars["DA40_XLS_AUTOMIXTURE"].Name);
        Assert.Equal("number", vars["DA40_XLS_AUTOMIXTURE"].Units);
        Assert.True(vars["DA40_XLS_AUTOMIXTURE"].RenderAsReadOnlyStatus);

        // It carries a Ctrl+M row: the call-out is a state change and must be mutable.
        Assert.False(vars["DA40_XLS_AUTOMIXTURE"].ExcludeFromMonitorManager);

        // Read-only: the setting lives in the simulator's assistance options, not here.
        Assert.DoesNotContain("DA40_XLS_AUTOMIXTURE",
            Xls().GetPanelControls().SelectMany(p => p.Value));
    }

    /// <summary>The NG has no such variable — it is a FADEC, there is no mixture lever.</summary>
    [Fact]
    public void TheNgHasNoAutomixtureRow()
    {
        Assert.False(Ng().GetVariables().ContainsKey("DA40_XLS_AUTOMIXTURE"));
        Assert.DoesNotContain(Ng().GetVariables().Values, d => d.Name == "AUTOMIXTURE");
    }

    /// <summary>
    /// ⚠️ THE VALUE RAMPS IN FIFTHS, so only the ends mean anything. The model adds 0.2 a
    /// tick until it reaches 1 and drops to 0 in one step; a reading in between is the
    /// ramp, not a state, and must never be announced as one.
    /// </summary>
    [Theory]
    [InlineData(0, "Off")]
    [InlineData(1, "On")]
    [InlineData(1.0, "On")]
    [InlineData(0.2, "Engaging")]
    [InlineData(0.8, "Engaging")]
    public void TheAutomixtureRampReadsAsARampAndNotAsAState(double value, string expected)
        => Assert.Equal(expected, CowsDA40Definition.DescribeAutomixture(value));

    // ------------------------------------------------- Engage Starter with Mixture (XLS)

    /// <summary>
    /// The POH prints the same MFD menu for both airframes and they differ by exactly two
    /// rows: Priming Assist (which the Priming panel already owns) and this one. A
    /// variant-blind options list was offering the NG a switch its aeroplane has not got.
    /// </summary>
    [Fact]
    public void OnlyTheXlsCarriesTheMixtureStarterOption()
    {
        var xls = Xls().GetVariables();
        Assert.True(xls.ContainsKey("DA40_OPT_START_MIXTURE"));
        Assert.Equal("START_MIXTURE", xls["DA40_OPT_START_MIXTURE"].Name);

        Assert.False(Ng().GetVariables().ContainsKey("DA40_OPT_START_MIXTURE"));
    }

    /// <summary>
    /// ⚠️ IT NEEDS THE IGNITION AT BOTH AND THE HELP MUST SAY SO. The model's own gate is
    /// `INPUT_MIXTURE == 0` AND `STARTER_SWITCH == 3` (Both) or 4 AND `START_MIXTURE == 1`
    /// AND the engine under 500 rpm — so with the key anywhere else the option looks dead.
    /// </summary>
    [Fact]
    public void TheMixtureStarterOptionNamesItsIgnitionRequirement()
    {
        string help = Xls().GetVariables()["DA40_OPT_START_MIXTURE"].HelpText ?? "";
        Assert.Contains("Both", help);
    }

    // ------------------------------------------------- Emergency fuel transfer (NG)

    /// <summary>
    /// ⚠️ IN EMERGENCY THE AEROPLANE THROWS FUEL AWAY AND NOTHING SAID SO. The POH:
    /// "There is no sensor to stop the transfer of fuel. Fuel will be pushed overboard if
    /// the transfer isn't stopped." The model agrees — the cooling loop returns fuel to the
    /// main tank clamped `19.5 min`, and past that clamp it is simply gone.
    /// </summary>
    [Fact]
    public void TheNgReportsTheEmergencyTransfer()
    {
        var vars = Ng().GetVariables();
        Assert.True(vars.ContainsKey("DA40_FUEL_XFER_EMERG"));
        Assert.Equal("FUEL_TEMP_ENG_FLOW:1", vars["DA40_FUEL_XFER_EMERG"].Name);
        Assert.Contains("DA40_FUEL_XFER_EMERG", Ng().GetPanelDisplayVariables()["Fuel System"]);

        // The XLS has neither this fuel system nor this variable.
        Assert.False(Xls().GetVariables().ContainsKey("DA40_FUEL_XFER_EMERG"));
    }

    /// <summary>
    /// It is batched so the row can be composed beside the valve and the tank, and SILENT
    /// because it is a rate — non-zero whenever the propeller turns, valve or no valve.
    /// </summary>
    [Fact]
    public void TheEmergencyTransferRateIsCachedAndNeverSpokenAsANumber()
    {
        var def = Ng().GetVariables()["DA40_FUEL_XFER_EMERG"];
        Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
        Assert.True(def.IsAnnounced);
        Assert.Contains("DA40_FUEL_XFER_EMERG", CowsDA40Definition.SilentCachedReadoutKeys);
    }

    [Fact]
    public void TheTransferRowSaysNothingBeforeItHasReadTheValve()
        => Assert.Equal("Not available yet",
            CowsDA40Definition.DescribeEmergencyTransfer(null, 0.0125, 10));

    [Theory]
    [InlineData(0)]   // Main
    [InlineData(2)]   // Off
    public void NothingTransfersUnlessTheValveIsAtEmergency(double valve)
        => Assert.Equal("Not transferring",
            CowsDA40Definition.DescribeEmergencyTransfer(valve, 0.0125, 10));

    /// <summary>
    /// The rate is the model's own per-SECOND figure. 0.0125 gal/s is what
    /// `PROP RPM / 2300 × 45 / 3600` gives at 2300 rpm — the POH's "around 0.7 gal/min".
    /// </summary>
    [Fact]
    public void TheTransferRateIsReportedPerMinute()
    {
        string text = CowsDA40Definition.DescribeEmergencyTransfer(1, 0.0125, 10);
        Assert.Contains("0.8 gallons per minute", text);
        Assert.DoesNotContain("overboard", text);
    }

    /// <summary>
    /// The one that matters: against the 19.5 gallon stop, everything the loop pushes in
    /// from here is lost, and there is no sensor and no gauge that can show it (the NG's
    /// fuel indication saturates at 14 gallons, well below the stop).
    /// </summary>
    [Fact]
    public void AFullMainTankMeansTheTransferIsGoingOverboard()
    {
        string text = CowsDA40Definition.DescribeEmergencyTransfer(1, 0.0125, 19.5);
        Assert.Contains("overboard", text);
    }

    // ------------------------------------------------------------------ The POH's prose

    /// <summary>
    /// ⚠️ A COLD DAY CAPS THE NG's FULL-POWER RPM AT 2100. The model gates the 92-to-100 %
    /// branch on `AMBIENT TEMPERATURE > -10 °C` and otherwise writes a flat 2100, so full
    /// power on a cold morning reads 200 rpm low and looks like a governor fault. ⚠️ The
    /// POH's text says minus ten and its own chart bubble says ten; the MODEL settles it.
    /// </summary>
    [Fact]
    public void TheCommandedRpmRowNamesTheColdWeatherCap()
    {
        string help = Ng().GetVariables()["DA40_POWER_TARGET_RPM"].HelpText ?? "";
        Assert.Contains("2100", help);
        Assert.Contains("minus 10", help);
    }

    /// <summary>
    /// The EGT bars carry no arc, so there is no band to annotate and no colour a sighted
    /// pilot reads either. The POH's 1350 °F is a sentence, and it stays a sentence rather
    /// than becoming an invented arc.
    /// </summary>
    [Fact]
    public void TheHottestEgtCarriesThePohsRecommendedCeiling()
    {
        string help = Xls().GetVariables()["DA40_XLS_EGT_HOT"].HelpText ?? "";
        Assert.Contains("1350", help);
    }

    /// <summary>
    /// The escape from the unstartable-engine trap, which no variable on the Engine
    /// Variation panel can express: the POH says the variations are "regenerated by
    /// resetting the engine damage in the G1000 engine page menu", and by nothing else.
    /// </summary>
    [Theory]
    [InlineData("DA40_XLS_VAR_SET")]
    [InlineData("DA40_XLS_VAR_CYL_SET")]
    public void TheVariationLatchesNameTheirOwnWayOut(string key)
    {
        string help = Xls().GetVariables()[key].HelpText ?? "";
        Assert.Contains("Reset: Damage", help);
    }

    /// <summary>
    /// What state saving does NOT carry is not guessable from the switch, and the POH lists
    /// it: canopy and window positions, parking position, the alternator masters, the
    /// ignition switch position, and the electric, alternator and engine masters.
    /// </summary>
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void StateSavingNamesWhatItDoesNotSave(DA40Variant variant)
    {
        string help = new CowsDA40Definition(variant)
            .GetVariables()["DA40_OPT_STATE_SAVING"].HelpText ?? "";
        Assert.Contains("Masters", help);
        Assert.Contains("ignition", help);
    }

    /// <summary>
    /// The damage causes are the POH's own list and they DIFFER by airframe: shock cooling
    /// and lead fouling are the Lycoming's, sustained load above 92 % and a poor cooldown
    /// are the Austro's. The water-cooled NG is immune to the shock cooling the XLS is not,
    /// so listing the XLS's causes on the NG would be describing the wrong engine.
    /// </summary>
    [Fact]
    public void TheDamageCausesAreTheOnesThatAirframeActuallyHas()
    {
        string ng = Ng().GetVariables()["DA40_OPT_DAMAGE"].HelpText ?? "";
        string xls = Xls().GetVariables()["DA40_OPT_DAMAGE"].HelpText ?? "";

        Assert.Contains("92 percent", ng);
        Assert.DoesNotContain("shock cooling", ng);

        Assert.Contains("shock cooling", xls);
        Assert.Contains("lead fouling", xls);
        Assert.DoesNotContain("92 percent", xls);
    }
}
