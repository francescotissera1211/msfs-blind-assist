using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Structural checks for the COWS DA40 panel tree.
///
/// The load-bearing one is <see cref="EveryPanelHasAControlsEntry"/>: MainForm's panel
/// build returns early for any panel missing from GetPanelControls(), so a panel named
/// in GetPanelStructure() but absent from BuildPanelControls() renders COMPLETELY BLANK
/// with no error anywhere. That trap cost the HS787 seven empty Flight Data panels
/// (docs/hs787.md), and it is silent — only a test catches it.
/// </summary>
public class CowsDA40PanelStructureTests
{
    private static CowsDA40Definition Ng() => new(DA40Variant.NG);
    private static CowsDA40Definition Xls() => new(DA40Variant.XLS);

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPanelHasAControlsEntry(DA40Variant variant)
    {
        var def = new CowsDA40Definition(variant);
        var controls = def.GetPanelControls();

        var missing = def.GetPanelStructure()
                         .SelectMany(section => section.Value)
                         .Where(panel => !controls.ContainsKey(panel))
                         .ToList();

        Assert.True(missing.Count == 0,
            $"{variant}: these panels would render blank — {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void PanelNamesAreUniqueAcrossSections(DA40Variant variant)
    {
        // Panel names key a flat dictionary, so a duplicate in two sections silently
        // collapses into one entry and one of the two sections loses its panel.
        var all = new CowsDA40Definition(variant)
            .GetPanelStructure().SelectMany(s => s.Value).ToList();

        var dupes = all.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(dupes.Count == 0, $"{variant}: duplicate panel names — {string.Join(", ", dupes)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoSectionIsEmpty(DA40Variant variant)
    {
        var empty = new CowsDA40Definition(variant)
            .GetPanelStructure().Where(s => s.Value.Count == 0).Select(s => s.Key).ToList();

        Assert.True(empty.Count == 0, $"{variant}: empty sections — {string.Join(", ", empty)}");
    }

    [Fact]
    public void BothVariantsShareTheCommonSections()
    {
        var expected = new[]
        {
            "Instrument Panel", "Center Console", "Circuit Breakers",
            "G1000 PFD", "G1000 MFD", "Autopilot", "Cabin", "Simulation"
        };

        Assert.Equal(expected, Ng().GetPanelStructure().Keys.ToArray());
        Assert.Equal(expected, Xls().GetPanelStructure().Keys.ToArray());
    }

    [Fact]
    public void NgHasTheFadecPanelsAndNotTheLycomingOnes()
    {
        var ng = Ng().GetPanelStructure();

        Assert.Contains("ECU", ng["Instrument Panel"]);
        Assert.Contains("Fuel Transfer", ng["Center Console"]);

        Assert.DoesNotContain("Magnetos", ng["Instrument Panel"]);
        Assert.DoesNotContain("Mixture and Propeller", ng["Center Console"]);
        Assert.DoesNotContain("Priming", ng["Center Console"]);
        Assert.DoesNotContain("Lean Assist", ng["G1000 MFD"]);
    }

    [Fact]
    public void XlsHasTheLycomingPanelsAndNotTheFadecOnes()
    {
        var xls = Xls().GetPanelStructure();

        Assert.Contains("Magnetos", xls["Instrument Panel"]);
        Assert.Contains("Mixture and Propeller", xls["Center Console"]);
        Assert.Contains("Priming", xls["Center Console"]);
        Assert.Contains("Lean Assist", xls["G1000 MFD"]);

        Assert.DoesNotContain("ECU", xls["Instrument Panel"]);
        Assert.DoesNotContain("Fuel Transfer", xls["Center Console"]);
    }

    [Fact]
    public void VariantIdentityIsReported()
    {
        Assert.Equal("COWS_DA40NG", Ng().AircraftCode);
        Assert.Equal("COWS_DA40XLS", Xls().AircraftCode);
        Assert.Equal("COWS Diamond DA40-NG", Ng().AircraftName);
        Assert.Equal("COWS Diamond DA40-XLS", Xls().AircraftName);
    }

    [Fact]
    public void AltitudeIsIncrementDecrement_BecauseTheGfc700HasNoAbsoluteSet()
    {
        // Measured on the aircraft: AP_ALT_VAR_SET_ENGLISH ignores its parameter and
        // adds +1000 ft; AP_ALT_VAR_INC/_DEC are +/-100 ft. There is no absolute set,
        // so SetValue would silently do the wrong thing.
        Assert.Equal(MSFSBlindAssist.Aircraft.FCUControlType.IncrementDecrement,
                     Ng().GetAltitudeControlType());
    }

    [Fact]
    public void VisualGuidanceIsScaledForLightGa_NotTheA320Defaults()
    {
        var p = Ng().GetVisualGuidanceProfile();

        Assert.Equal(72.0, p.ReferenceVrefKnots);          // A320 default is 140
        Assert.True(p.MaxPitchRateDegPerSec > 2.5);        // light airframe, more authority
        Assert.True(p.TonePitchRangeDeg > 6.0);            // wider GA attitude envelope
    }

    // ==============================================================================
    // Electrical panel
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryPanelControlKeyExistsInGetVariables(DA40Variant variant)
    {
        // A key listed in a panel but absent from GetVariables() renders as a missing
        // control with no error — the same silent class of failure as the blank panel.
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        var missing = def.GetPanelControls()
                         .SelectMany(p => p.Value.Select(k => new { Panel = p.Key, Key = k }))
                         .Where(x => !vars.ContainsKey(x.Key))
                         .Select(x => $"{x.Panel}/{x.Key}")
                         .ToList();

        Assert.True(missing.Count == 0, $"{variant}: undefined control keys — {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryDisplayVariableKeyExistsInGetVariables(DA40Variant variant)
    {
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        var missing = def.GetPanelDisplayVariables()
                         .SelectMany(p => p.Value.Select(k => new { Panel = p.Key, Key = k }))
                         .Where(x => !vars.ContainsKey(x.Key))
                         .Select(x => $"{x.Panel}/{x.Key}")
                         .ToList();

        Assert.True(missing.Count == 0, $"{variant}: undefined display keys — {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryDisplayPanelAlsoHasAControlsEntry(DA40Variant variant)
    {
        // Display-only panels still need a BuildPanelControls entry or MainForm never
        // builds them and the display vars never render.
        var def = new CowsDA40Definition(variant);
        var controls = def.GetPanelControls();

        var missing = def.GetPanelDisplayVariables().Keys
                         .Where(panel => !controls.ContainsKey(panel)).ToList();

        Assert.True(missing.Count == 0, $"{variant}: display panels with no controls entry — {string.Join(", ", missing)}");
    }

    [Fact]
    public void ElectricalPanelExposesEverySwitchOnThePanel()
    {
        var controls = Ng().GetPanelControls()["Electrical"];

        Assert.Contains("DA40_ELEC_MASTER_BATTERY", controls);
        Assert.Contains("DA40_ELEC_AVIONICS_MASTER", controls);
        Assert.Contains("DA40_ELEC_ESS_BUS", controls);
        Assert.Contains("DA40_ELEC_EMER_BATT", controls);
        Assert.Contains("DA40_ELEC_EMER_BATT_COVER", controls);
        // Engine Master is NOT here — it belongs to Engine Start.
        Assert.DoesNotContain("DA40_START_ENGINE_MASTER", controls);
        Assert.Equal(5, controls.Count);
    }

    [Fact]
    public void ElectricalPanelHasNoAlternatorSwitch()
    {
        // The AE300's alternator is ECU-controlled — there is no switch. STATE_ALTERNATOR
        // reads 0 while the alternator produces 28 A, so offering it as a toggle would be
        // both non-functional and a lie about the aeroplane.
        var vars = Ng().GetVariables();

        Assert.DoesNotContain(vars, kv => kv.Value.Name == "STATE_ALTERNATOR");
    }

    [Fact]
    public void ElectricalStatusDisplayCoversEveryBusAndBattery()
    {
        var display = Ng().GetPanelDisplayVariables()["Electrical"];

        // Per-bus, not a single "electrical OK" — raw values are the point.
        Assert.Contains("DA40_ELEC_BUS_MAIN_VOLT", display);
        Assert.Contains("DA40_ELEC_BUS_ESS_VOLT", display);
        Assert.Contains("DA40_ELEC_BUS_EMER_VOLT", display);
        Assert.Contains("DA40_ELEC_BUS_HOT_VOLT", display);
        Assert.Contains("DA40_ELEC_BUS_ECU1_VOLT", display);

        // All three batteries.
        Assert.Contains("DA40_ELEC_BATT_PERCENT", display);
        Assert.Contains("DA40_ELEC_BATT_ECU_PERCENT", display);
        Assert.Contains("DA40_ELEC_BATT_EMER_VOLT", display);

        Assert.Contains("DA40_ELEC_ALT_AMPS", display);
    }

    [Fact]
    public void ElectricalReadoutsAreReadOnlyAndCarryUnits()
    {
        var vars = Ng().GetVariables();

        foreach (var key in Ng().GetPanelDisplayVariables()["Electrical"])
        {
            var v = vars[key];
            Assert.True(v.RenderAsReadOnlyStatus, $"{key} should be a read-only status field");
            Assert.False(string.IsNullOrWhiteSpace(v.Units), $"{key} has no units");
            Assert.False(string.IsNullOrWhiteSpace(v.DisplayName), $"{key} has no display name");
        }
    }

    [Fact]
    public void ElectricalSwitchesAreTwoStateCombosWithLabels()
    {
        var vars = Ng().GetVariables();

        foreach (var key in Ng().GetPanelControls()["Electrical"])
        {
            var v = vars[key];
            Assert.Equal(2, v.ValueDescriptions.Count);
            Assert.False(v.RenderAsReadOnlyStatus, $"{key} must be settable");
            // Screen readers announce combo changes themselves; a def-side announce
            // on top of that is the double-talk CLAUDE.md forbids.
            Assert.False(v.IsAnnounced, $"{key} must not self-announce on UI set");
        }
    }

    // ==============================================================================
    // Engine Start panel (NG)
    // ==============================================================================

    [Fact]
    public void EngineStartExposesTheKeyAndTheEngineMaster()
    {
        var controls = Ng().GetPanelControls()["Engine Start"];

        Assert.Contains("DA40_START_STARTER_ENGAGE", controls);
        Assert.Contains("DA40_START_STARTER_RELEASE", controls);
        // The AFM legend groups the Engine Master with the engine controls, so it lives
        // here — and ONLY here.
        Assert.Contains("DA40_START_ENGINE_MASTER", controls);
        Assert.Contains("DA40_START_ENGINE_MASTER_COVER", controls);
    }

    [Fact]
    public void StartKeyPositionIsReadOnly_BecauseTheLvarIsADerivedMirror()
    {
        // Writing L:STARTER_SWITCH does nothing (measured: wrote 1, read back 0). It is
        // computed from the battery master and the starter, so it can only be a readout.
        var v = Ng().GetVariables()["DA40_START_KEY_POSITION"];

        Assert.True(v.RenderAsReadOnlyStatus);
        Assert.Equal("STARTER_SWITCH", v.Name);
        Assert.Equal(3, v.ValueDescriptions.Count);
        Assert.Equal("Start", v.ValueDescriptions[2]);
    }

    [Fact]
    public void StarterButtonsAreMomentaryAndCarryTheAfmLimit()
    {
        var vars = Ng().GetVariables();

        foreach (var key in new[] { "DA40_START_STARTER_ENGAGE", "DA40_START_STARTER_RELEASE" })
        {
            var v = vars[key];
            Assert.True(v.RenderAsButton, $"{key} should be a button");
            Assert.True(v.SuppressRestingButtonState, $"{key} has no meaningful resting value");
            Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Never, v.UpdateFrequency);
        }

        Assert.Contains("10", vars["DA40_START_STARTER_ENGAGE"].HelpText);
    }

    [Fact]
    public void EngineStartScanCoversTheAfmStartChecks()
    {
        var display = Ng().GetPanelDisplayVariables()["Engine Start"];

        // AFM 4A.5.3: glow, then crank, then oil pressure out of the red within 3 s,
        // then idle RPM 710 +/- 30.
        Assert.Contains("DA40_START_GLOW_ON", display);
        Assert.Contains("DA40_START_STARTER_ENGAGED", display);
        Assert.Contains("DA40_START_COMBUSTION", display);
        Assert.Contains("DA40_START_OIL_PRESSURE", display);
        Assert.Contains("DA40_START_RPM", display);
        Assert.Contains("DA40_START_GEARBOX_TEMP", display);
    }

    [Fact]
    public void XlsHasNoNgEngineStartVariables()
    {
        // The XLS starts on magnetos and a primer, not a FADEC glow-plug cycle.
        var vars = Xls().GetVariables();

        Assert.DoesNotContain("DA40_START_GLOW_ON", vars.Keys);
        Assert.DoesNotContain("DA40_START_STARTER_ENGAGE", vars.Keys);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NoControlAppearsInTwoPanels(DA40Variant variant)
    {
        // One control, one home. A variable reachable from two panels is disorienting
        // with a screen reader and makes "where is that switch" ambiguous.
        var dupes = new CowsDA40Definition(variant).GetPanelControls()
            .SelectMany(p => p.Value.Select(k => new { p.Key, Var = k }))
            .GroupBy(x => x.Var)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} in {string.Join(" + ", g.Select(x => x.Key))}")
            .ToList();

        Assert.True(dupes.Count == 0, $"{variant}: duplicated controls — {string.Join("; ", dupes)}");
    }

    // ==============================================================================
    // ECU panel (NG)
    // ==============================================================================

    [Fact]
    public void EcuPanelHasTheVoterAndTheTest()
    {
        var controls = Ng().GetPanelControls()["ECU"];

        Assert.Contains("DA40_ECU_VOTER", controls);
        Assert.Contains("DA40_ECU_TEST", controls);
        Assert.Equal(2, controls.Count);
    }

    [Fact]
    public void EcuVoterUsesTheModelsOwnLabelOrder_NotTheIntuitiveOne()
    {
        // The obvious guess is A / AUTO / B. The model's ANIMTIPs say otherwise, and the
        // tooltips are what a sighted pilot actually reads off the switch.
        var v = Ng().GetVariables()["DA40_ECU_VOTER"];

        Assert.Equal("ECU B", v.ValueDescriptions[0]);
        Assert.Equal("Auto",  v.ValueDescriptions[1]);
        Assert.Equal("ECU A", v.ValueDescriptions[2]);
    }

    [Fact]
    public void EcuTestIsAButtonAndNotAToggle()
    {
        // ECU_TEST:1 is a *_Held button the airframe zeroes every frame; a combo would
        // write once and the test would never run.
        var v = Ng().GetVariables()["DA40_ECU_TEST"];

        Assert.True(v.RenderAsButton);
        Assert.True(v.SuppressRestingButtonState);
        Assert.Empty(v.ValueDescriptions);
    }

    [Fact]
    public void EcuScanShowsAllFiveAfmPreconditions()
    {
        var display = Ng().GetPanelDisplayVariables()["ECU"];

        Assert.Contains("DA40_ECU_PRE_POWER_LEVER", display);
        Assert.Contains("DA40_ECU_PRE_PROP_RPM", display);
        Assert.Contains("DA40_ECU_PRE_GEARBOX", display);
        Assert.Contains("DA40_ECU_PRE_ON_GROUND", display);
        // The fifth (voter in Auto) is the control itself, on the same panel.
        Assert.Contains("DA40_ECU_VOTER", Ng().GetPanelControls()["ECU"]);
    }

    [Fact]
    public void EcuScanDistinguishesLatchedFromUnlatchedFaults()
    {
        // The POH's whole ECU-failure procedure turns on this distinction: an unlatched
        // error clears with a voter cycle, a latched one only via the MFD Reset: ECU.
        var display = Ng().GetPanelDisplayVariables()["ECU"];

        Assert.Contains("DA40_ECU_FAIL_A", display);
        Assert.Contains("DA40_ECU_FAIL_B", display);
        Assert.Contains("DA40_ECU_LATCH_A", display);
        Assert.Contains("DA40_ECU_LATCH_B", display);
    }

    [Fact]
    public void XlsHasNoEcuPanelOrVariables()
    {
        Assert.DoesNotContain("ECU", Xls().GetPanelStructure()["Instrument Panel"]);
        Assert.DoesNotContain("DA40_ECU_VOTER", Xls().GetVariables().Keys);
    }
}
