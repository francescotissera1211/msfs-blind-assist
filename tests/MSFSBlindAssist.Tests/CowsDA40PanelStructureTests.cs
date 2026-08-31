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
        Assert.Contains("DA40_ELEC_ENGINE_MASTER", controls);
        Assert.Contains("DA40_ELEC_ENGINE_MASTER_COVER", controls);
        Assert.Contains("DA40_ELEC_ESS_BUS", controls);
        Assert.Contains("DA40_ELEC_EMER_BATT", controls);
        Assert.Contains("DA40_ELEC_EMER_BATT_COVER", controls);
        Assert.Equal(7, controls.Count);
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
}
