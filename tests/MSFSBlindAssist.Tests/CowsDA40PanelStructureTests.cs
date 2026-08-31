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
        Assert.Contains("DA40_ECU_PROP_SENSED", display);
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

    // ==============================================================================
    // Lighting Switches panel
    // ==============================================================================

    [Fact]
    public void LightingCoversEverySwitchTheAfmNames()
    {
        // AFM abbreviation list: LANDING, TAXI/MAP, POSITION, STROBE, INST. LT, FLOOD.
        var controls = Ng().GetPanelControls()["Lighting Switches"];

        Assert.Contains("DA40_LIGHT_LANDING", controls);
        Assert.Contains("DA40_LIGHT_TAXI", controls);
        Assert.Contains("DA40_LIGHT_POSITION", controls);
        Assert.Contains("DA40_LIGHT_STROBE", controls);
        Assert.Contains("DA40_LIGHT_INSTRUMENT", controls);
        Assert.Contains("DA40_LIGHT_FLOOD", controls);
    }

    [Fact]
    public void EachCabinLightIsIndividuallySwitchable()
    {
        // Three overhead switches on the aeroplane, so three controls here. The
        // all-at-once shortcut is an extra, never a replacement.
        var controls = Ng().GetPanelControls()["Lighting Switches"];

        Assert.Contains("DA40_LIGHT_CABIN_RIGHT", controls);
        Assert.Contains("DA40_LIGHT_CABIN_LEFT", controls);
        Assert.Contains("DA40_LIGHT_CABIN_BAGGAGE", controls);
        // The COWS all-at-once clickspot is a mouse shortcut, not a panel control, and
        // the three switches already do everything it does.
        Assert.DoesNotContain("DA40_LIGHT_CABIN_ALL", controls);
        Assert.Equal(9, controls.Count);
    }

    [Fact]
    public void InstrumentAndFloodAreBrightnessKnobs_NotOnOffSwitches()
    {
        // AFM legend item 10 is "Rotary buttons for instrument lighting and flood light",
        // and the model drives them from LIGHT POTENTIOMETER:3 and :5 as percentages.
        var vars = Ng().GetVariables();

        foreach (var key in new[] { "DA40_LIGHT_INSTRUMENT", "DA40_LIGHT_FLOOD" })
        {
            var v = vars[key];
            Assert.True(v.RenderAsSlider, $"{key} should be a brightness slider");
            Assert.Equal(0, v.SliderMin);
            Assert.Equal(100, v.SliderMax);
            Assert.Empty(v.ValueDescriptions);
        }

        Assert.Equal("LIGHT POTENTIOMETER:3", vars["DA40_LIGHT_INSTRUMENT"].Name);
        Assert.Equal("LIGHT POTENTIOMETER:5", vars["DA40_LIGHT_FLOOD"].Name);
    }

    [Fact]
    public void IceLightIsStatusOnly_ThereIsNoSwitchForIt()
    {
        var def = Ng();

        Assert.Contains("DA40_LIGHT_ICE_STATE", def.GetPanelDisplayVariables()["Lighting Switches"]);
        Assert.DoesNotContain("DA40_LIGHT_ICE_STATE", def.GetPanelControls()["Lighting Switches"]);
    }

    [Fact]
    public void EcuShowsOnePropellerReading_NotTwoNearIdenticalOnes()
    {
        var def = Ng();

        Assert.Contains("DA40_ECU_PROP_SENSED", def.GetPanelDisplayVariables()["ECU"]);
        Assert.DoesNotContain("DA40_ECU_PRE_PROP_RPM", def.GetVariables().Keys);
        // The sensed speed is the one the stage machine gates on.
        Assert.Equal("PROP_RPM_SENS:1", def.GetVariables()["DA40_ECU_PROP_SENSED"].Name);
    }

    [Fact]
    public void EveryLightSwitchIsTwoPosition()
    {
        // No multi-position light switches exist on this airframe; the only three-position
        // switches are the ECU voter, the ignition key and the fuel selector.
        var vars = Ng().GetVariables();

        foreach (var key in Ng().GetPanelControls()["Lighting Switches"])
        {
            var v = vars[key];
            if (v.RenderAsSlider) continue;   // the two brightness knobs
            Assert.Equal(2, v.ValueDescriptions.Count);
        }
    }

    // ==============================================================================
    // Ice and Pitot panel
    // ==============================================================================

    [Fact]
    public void IceAndPitotHasAllThreeControls()
    {
        var controls = Ng().GetPanelControls()["Ice and Pitot"];

        Assert.Contains("DA40_ICE_PITOT_HEAT", controls);
        // Not on the AFM legend, but an OPEN/CLOSED item throughout the procedures.
        Assert.Contains("DA40_ICE_ALTERNATE_AIR", controls);
        Assert.Contains("DA40_ICE_ALTERNATE_STATIC", controls);
        Assert.Equal(3, controls.Count);
    }

    [Fact]
    public void IceLightStaysOnLighting_NotDuplicatedHere()
    {
        var def = Ng();

        Assert.Contains("DA40_LIGHT_ICE_STATE", def.GetPanelDisplayVariables()["Lighting Switches"]);
        Assert.DoesNotContain("DA40_LIGHT_ICE_STATE", def.GetPanelDisplayVariables()["Ice and Pitot"]);
    }

    [Fact]
    public void InductionAirFactorIsOnTheScan_SoTheDoorCanBeSeenWorking()
    {
        // Opening alternate air moved ENG_ALT_AIR_FACTOR from 1.00 to 0.98 live; without
        // it there is no way to tell the door did anything.
        Assert.Contains("DA40_ICE_ALT_AIR_FACTOR", Ng().GetPanelDisplayVariables()["Ice and Pitot"]);
    }

    // ==============================================================================
    // Auto-announce and the Monitor Manager
    // ==============================================================================

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EverySettableSwitchAnnouncesExternalChanges(DA40Variant variant)
    {
        // A switch thrown in the cockpit, by hardware, or by a failure MUST speak.
        // IsAnnounced governs BACKGROUND changes only — MSFSBA's own combo sets are
        // suppressed by the global _uiSetEcho wrap, so this does not cause double-talk.
        // Continuous is required as well: OnRequest vars are never polled, so an external
        // change is simply never seen.
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        var silent = def.GetPanelControls()
            .SelectMany(p => p.Value)
            .Distinct()
            .Select(k => new { Key = k, Def = vars[k] })
            // Buttons are momentary and have no state to announce; sliders are numeric.
            .Where(x => !x.Def.RenderAsButton && !x.Def.RenderAsSlider)
            .Where(x => !x.Def.IsAnnounced
                     || x.Def.UpdateFrequency != MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous)
            .Select(x => x.Key)
            .ToList();

        Assert.True(silent.Count == 0,
            $"{variant}: these switches will not announce external changes — {string.Join(", ", silent)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NumericReadoutsStaySilent(DA40Variant variant)
    {
        // Volts, temperatures and RPM change constantly. Announcing them would bury the
        // changes that matter; they are read on demand from the status display instead.
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        var noisy = def.GetPanelDisplayVariables()
            .SelectMany(p => p.Value)
            .Distinct()
            .Where(k => vars[k].IsAnnounced)
            .ToList();

        Assert.True(noisy.Count == 0,
            $"{variant}: these readouts would speak on every change — {string.Join(", ", noisy)}");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryAnnouncedSwitchGetsAMonitorManagerRow(DA40Variant variant)
    {
        // Ctrl+M must be able to mute anything that speaks, or the pilot has no control
        // over the chatter. MonitorRowBuilder lists Continuous + IsAnnounced vars.
        var def = new CowsDA40Definition(variant);
        var rows = MSFSBlindAssist.Services.MonitorRowBuilder.Build(def.GetVariables());
        var rowKeys = rows.Select(r => r.Key).ToHashSet();

        var announced = def.GetVariables()
            .Where(kv => kv.Value.IsAnnounced
                      && kv.Value.UpdateFrequency == MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous
                      && !kv.Value.ExcludeFromMonitorManager)
            .Select(kv => kv.Key);

        foreach (var key in announced)
        {
            Assert.True(rowKeys.Contains(key), $"{variant}: {key} announces but has no Ctrl+M row");
        }

        Assert.NotEmpty(rows);
    }

    [Fact]
    public void MonitorRowsCarryReadableLabels()
    {
        var rows = MSFSBlindAssist.Services.MonitorRowBuilder.Build(Ng().GetVariables());

        foreach (var row in rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Label));
            // The label should be the display name, not a raw DA40_ key.
            Assert.False(row.Label.StartsWith("DA40_"), $"{row.Key} shows a raw key as its label");
        }
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void ScansDoNotRepeatControls(DA40Variant variant)
    {
        // A control already reads its own position when you tab to it. Repeating it on
        // the scan is duplication, and it drags an announcing variable into a list that
        // is supposed to be silent.
        var def = new CowsDA40Definition(variant);
        var controls = def.GetPanelControls().SelectMany(p => p.Value).ToHashSet();

        var repeated = def.GetPanelDisplayVariables()
            .SelectMany(p => p.Value)
            .Where(controls.Contains)
            .Distinct()
            .ToList();

        Assert.True(repeated.Count == 0,
            $"{variant}: controls repeated on a scan — {string.Join(", ", repeated)}");
    }

    // ==============================================================================
    // Standby Instruments panel
    // ==============================================================================

    [Fact]
    public void StandbyHasItsOwnAltimeterSubscale()
    {
        // KOHLSMAN SETTING HG:2 is a SEPARATE subscale from the G1000's, which is why the
        // AFM descent check reads "Altimeters (2) SET".
        var v = Ng().GetVariables()["DA40_STBY_ALTIMETER_SET"];

        Assert.Equal("KOHLSMAN SETTING HG:2", v.Name);
        // Typed entry, not a slider — see StandbyAltimeterIsTypedNotASlider. The 28.00 to
        // 31.50 travel is enforced on the write instead, where it belongs.
        Assert.False(v.RenderAsSlider);
    }

    [Fact]
    public void GyroCageIsAHeldButton_NotAToggle()
    {
        // ATT_CAGE is zeroed every frame; a combo would write once and nothing would happen.
        var v = Ng().GetVariables()["DA40_STBY_GYRO_CAGE"];

        Assert.True(v.RenderAsButton);
        Assert.True(v.SuppressRestingButtonState);
        Assert.Empty(v.ValueDescriptions);
    }

    [Fact]
    public void StandbyScanReportsGyroHealth_NotJustAttitude()
    {
        // A toppled gyro shows a plausible lie. Spin, rigidity and topple are the only way
        // to know the instrument has stopped being trustworthy.
        var display = Ng().GetPanelDisplayVariables()["Standby Instruments"];

        Assert.Contains("DA40_STBY_GYRO_PITCH", display);
        Assert.Contains("DA40_STBY_GYRO_BANK", display);
        Assert.Contains("DA40_STBY_GYRO_SPEED", display);
        Assert.Contains("DA40_STBY_GYRO_TOPPLE", display);
        Assert.Contains("DA40_STBY_GYRO_CAGED", display);
    }

    [Fact]
    public void StandbyCoversAllFourBackupInstruments()
    {
        // AFM legend 17-20: airspeed, artificial horizon, altimeter, compass.
        var display = Ng().GetPanelDisplayVariables()["Standby Instruments"];

        Assert.Contains("DA40_STBY_AIRSPEED", display);
        Assert.Contains("DA40_STBY_ALTITUDE", display);
        Assert.Contains("DA40_STBY_COMPASS", display);
        Assert.Contains("DA40_STBY_GYRO_PITCH", display);
    }

    [Fact]
    public void PropellerRpmUsesTheFineSource_NotTheQuantisedEisValue()
    {
        // DISP_PROP_RPM is rounded to 10 by the airframe (measured 710 against a true
        // 705.12). The needle moves smoothly, so the sensed value is what matches it.
        Assert.Equal("PROP_RPM_SENS:1", Ng().GetVariables()["DA40_START_RPM"].Name);
    }

    // ==============================================================================
    // Annunciators panel
    // ==============================================================================

    [Fact]
    public void AnnunciatorsAreReadOnly_BecauseALampIsAnOutput()
    {
        var def = Ng();

        Assert.Empty(def.GetPanelControls()["Annunciators"]);
        Assert.NotEmpty(def.GetPanelDisplayVariables()["Annunciators"]);
    }

    [Fact]
    public void AnnunciatorsCoverThePhysicalLampsAndWhatCanExtinguishThem()
    {
        // Three flap lights and the essential-bus lamp are the only real lamps on the
        // G1000 variant. A flap light can be dark for three different reasons, so the
        // breakers and the actual flap travel sit beside them.
        var display = Ng().GetPanelDisplayVariables()["Annunciators"];

        Assert.Contains("DA40_ANN_FLAP_UP", display);
        Assert.Contains("DA40_ANN_FLAP_TO", display);
        Assert.Contains("DA40_ANN_FLAP_LDG", display);
        Assert.Contains("DA40_ANN_ESS_BUS_VOLTS", display);
        Assert.Contains("DA40_ANN_CB_FLAP", display);
        Assert.Contains("DA40_ANN_FLAP_TRAVEL", display);
    }

    [Fact]
    public void StandbyAltimeterIsTypedNotASlider()
    {
        // MainForm's TrackBar is hardcoded 0-100 and maps the value as a percentage of the
        // slider range, so a subscale rendered as one reported "0 to 100" instead of
        // 28 to 31.5. The key ends in _SET, which gives a typed entry instead.
        var v = Ng().GetVariables()["DA40_STBY_ALTIMETER_SET"];

        Assert.False(v.RenderAsSlider);
        Assert.False(v.PreventTextInput);
        Assert.Contains("_SET", "DA40_STBY_ALTIMETER_SET");
    }

    [Fact]
    public void StandbyScanShowsIndicatedAndActualAttitude()
    {
        // The standby horizon drifts. Measured 2.2 degrees indicated against a true -3.0,
        // and the only way to notice is to compare the two.
        var display = Ng().GetPanelDisplayVariables()["Standby Instruments"];

        Assert.Contains("DA40_STBY_GYRO_PITCH", display);
        Assert.Contains("DA40_STBY_TRUE_PITCH", display);
        Assert.Contains("DA40_STBY_GYRO_BANK", display);
        Assert.Contains("DA40_STBY_TRUE_BANK", display);
    }
}
