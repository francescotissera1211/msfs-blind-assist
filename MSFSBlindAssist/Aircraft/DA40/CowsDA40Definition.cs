using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// COWS DA40 Series (DA40-NG and DA40-XLS) — MSFSBA's first GA airframe and its first
/// aircraft built on the stock Working Title G1000.
///
/// STUDY-LEVEL, NOT SIMPLIFIED. Every control a sighted pilot can reach is exposed
/// here: all circuit breakers individually, every switch, every lever, the complete
/// failure set. Derived readouts are additions, never substitutions for the raw value.
/// MSFSBA reports; it does not decide, gate, or auto-complete a pilot action.
///
/// TRANSPORTS (all verified live against a powered NG and XLS, 2026-08):
///   • SimVar          — airframe basics: flaps, trim, doors, lights, radios, XPDR, AP.
///   • L-var (MobiFlight) — everything COWS simulates: DISP_*, ELEC_*, STATE_*, CB_*.
///   • K:/H: events    — switches, levers, failure injection, G1000 bezel.
///   • Coherent CDP    — the G1000 DOM: CAS text, FMA, page menu, softkeys, checklist.
///
/// THREE AIRFRAME RULES THAT BREAK THE OBVIOUS DESIGN (all measured, do not "simplify"):
///   1. STATE_* L-vars are READ-ONLY MIRRORS. The model behaviour recomputes them every
///      frame, so a write is silently discarded (STATE_LIGHT_ICE=1 read back 0). Read
///      STATE_* (or the SimVar); WRITE the K: event or the real INPUT_*/CB_* L-var.
///   2. Momentary controls need a REPEATING write (~40 ms), not set-then-clear. A single
///      write is discarded; re-writing ATT_CAGE=1 every 40 ms held it and drove
///      ATT_GYRO_CAGE_SET 0→1. Hold durations: ECU test ~10 s, fuel-selector wire ~3 s.
///   3. The NG fuel valve is INTERLOCKED. Logic.xml forces it back to Normal every frame
///      while the copper break wire is intact, so FUEL_SELECTOR=1 reads back 0. Cut
///      FUEL_SELECTOR_WIRE_CUT first, then set the valve, then verify the readback.
///
/// This file currently defines the PANEL STRUCTURE ONLY. Controls are added panel by
/// panel in later passes; every panel below therefore has an (empty) BuildPanelControls
/// entry, because MainForm returns early for any panel missing from GetPanelControls()
/// and the panel would otherwise render completely blank (the HS787 Flight Data trap,
/// docs/hs787.md).
/// </summary>
public partial class CowsDA40Definition : BaseAircraftDefinition
{
    private readonly DA40Variant _variant;

    public CowsDA40Definition(DA40Variant variant = DA40Variant.NG)
    {
        _variant = variant;
    }

    /// <summary>Which airframe this instance is driving.</summary>
    public DA40Variant Variant => _variant;

    private bool IsNG => _variant == DA40Variant.NG;

    public override string AircraftName =>
        IsNG ? "COWS Diamond DA40-NG" : "COWS Diamond DA40-XLS";

    public override string AircraftCode =>
        IsNG ? "COWS_DA40NG" : "COWS_DA40XLS";

    // ==================================================================================
    // Panel structure — Sections → Panels
    // ==================================================================================

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var structure = new Dictionary<string, List<string>>
        {
            ["Instrument Panel"] = new List<string>
            {
                "Electrical",
                "Engine Start",
                "Lighting Switches",
                "Ice and Pitot",
                "Standby Instruments",
                "Annunciators"
            },
            ["Center Console"] = new List<string>
            {
                "Power and Levers",
                "Fuel System",
                "Flaps",
                "Elevator Trim",
                "Brakes",
                "Cabin Heat and Vent",
                "Audio"
            },
            // The section already says "Circuit Breakers"; repeating "CB" on every panel
            // just makes each one longer to hear. "Bus and Power" / "Airframe Systems"
            // avoid colliding with the Instrument Panel's "Electrical" and the Center
            // Console's panels — panel names key a FLAT dictionary, so a duplicate would
            // silently collapse the two into one (covered by a test).
            ["Circuit Breakers"] = new List<string>
            {
                "Engine and Fuel",
                "Flight Instruments",
                "Avionics",
                "Bus and Power",
                "Lighting",
                "Airframe Systems",
                "Copilot"
            },
            // The G1000 bezel — 12 softkeys, the FMS knobs, MENU/ENT/CLR/FPL/PROC/DIRECT-TO,
            // baro and range — is NOT a panel. It belongs in a dedicated display window
            // reached by an output-mode hotkey, the way the A380X E/WD (Alt+E), SD (Alt+S),
            // ND (Alt+N), PFD (Alt+P) and ISIS (Alt+I) already work: the window carries the
            // live display text AND the interactive keys together, and the softkey labels
            // change per page (Check <-> Next Item, Caution <-> Alerts) so they have to be
            // read live rather than laid out as a static control list.
            // The panels below are the SCANNABLE READOUTS only.
            ["G1000 PFD"] = new List<string>
            {
                "PFD Readout",
                "CAS Messages"
            },
            // Aircraft Options and the Electronic Checklist are NOT panels. Both are
            // interactive pages inside the MFD, driven by its softkeys and FMS knobs, so
            // they belong in the G1000 display window with the rest of the bezel. Anything
            // that can be done outside a panel does not get a panel.
            // What remains here is data a blind pilot cannot otherwise reach: the engine
            // indication strip and the fuel calculator, both L:var backed and scannable.
            ["G1000 MFD"] = new List<string>
            {
                "Engine Indication",
                "Fuel Calculator"
            },
            ["Autopilot"] = new List<string>
            {
                "GFC 700",
                "Flight Director"
            },
            ["Cabin"] = new List<string>
            {
                "Doors and Windows",
                "Seating and Payload"
            },
            ["Simulation"] = new List<string>
            {
                "Failures",
                "Reset",
                "Engine Damage"
            }
        };

        // Variant-specific panels. The NG has a FADEC/ECU and a Main/Auxiliary fuel
        // system with a transfer pump; the XLS has prop and mixture levers, magnetos,
        // priming and lean assist. Neither set is meaningful on the other airframe.
        if (IsNG)
        {
            structure["Instrument Panel"].Insert(2, "ECU");
            structure["Center Console"].Insert(2, "Fuel Transfer");
        }
        else
        {
            structure["Center Console"].Insert(1, "Mixture and Propeller");
            structure["Instrument Panel"].Insert(2, "Magnetos");
            structure["G1000 MFD"].Insert(1, "Lean Assist");
            structure["Center Console"].Insert(3, "Priming");
        }

        return structure;
    }

    // ==================================================================================
    // Panel controls
    //
    // Populated panel by panel in later passes. EVERY panel named in GetPanelStructure
    // MUST appear here even while empty: MainForm's panel build returns early for a
    // panel absent from GetPanelControls(), and the panel then renders nothing at all.
    // ==================================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = new Dictionary<string, List<string>>();

        foreach (var panels in GetPanelStructure().Values)
        {
            foreach (var panel in panels)
            {
                controls[panel] = new List<string>();
            }
        }

        // Populated panels override their empty placeholder.
        controls[ElectricalPanel] = new List<string>(ElectricalControls);
        controls[LightingPanel] = new List<string>(LightingControls);
        controls[IcePitotPanel] = new List<string>(IcePitotControls);
        if (IsNG) controls[EngineStartPanel] = new List<string>(EngineStartControls);
        if (IsNG) controls[EcuPanel] = new List<string>(EcuControls);

        return controls;
    }

    protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();

        foreach (var kv in BuildElectricalVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildLightingVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildIcePitotVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        if (IsNG)
        {
            foreach (var kv in BuildEngineStartVariables())
            {
                vars[kv.Key] = kv.Value;
            }

            foreach (var kv in BuildEcuVariables())
            {
                vars[kv.Key] = kv.Value;
            }
        }

        return vars;
    }

    /// <summary>
    /// Status-display contents, reached with Ctrl+3 and refreshed in place with F5.
    /// These are the PRIMARY readout for this aircraft: a sighted pilot flies the DA40
    /// by scanning gauges when they choose to look, so MSFSBA reproduces that scan
    /// rather than narrating values at the pilot. Announcements stay reserved for what
    /// interrupts a sighted pilot too (CAS messages, a breaker popping).
    /// </summary>
    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        var d = new Dictionary<string, List<string>>
        {
            [ElectricalPanel] = new List<string>(ElectricalDisplay),
            [LightingPanel] = new List<string>(LightingDisplay),
            [IcePitotPanel] = new List<string>(IcePitotDisplay)
        };

        if (IsNG) d[EngineStartPanel] = new List<string>(EngineStartDisplay);
        if (IsNG) d[EcuPanel] = new List<string>(EcuDisplay);

        return d;
    }

    public override Dictionary<string, string> GetButtonStateMapping()
        => new Dictionary<string, string>();

    /// <summary>
    /// Ctrl+M opens this aircraft's Monitor Manager, where the pilot ticks and un-ticks
    /// which background changes speak. Everything else falls through to the base.
    /// </summary>
    public override bool HandleHotkeyAction(
        Hotkeys.HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm,
        Hotkeys.HotkeyManager hotkeyManager)
    {
        if (action == Hotkeys.HotkeyAction.MonitorManager)
        {
            (parentForm as MainForm)?.ShowCowsDA40MonitorManagerDialog();
            return true;
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>
    /// Per-panel display text overrides, for fields MSFSBA computes rather than reads.
    /// </summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (TryGetEcuDisplayOverride(varKey, value, out displayText)) return true;

        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    /// <summary>
    /// Panel writes that need more than a plain set — chiefly the switches that expose
    /// only a toggle event and must be written conditionally so a combo is idempotent.
    /// Each panel adds its own handler; anything unclaimed falls through to the generic path.
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value,
        SimConnect.SimVarDefinition varDef, SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (HandleElectricalSet(varKey, value, simConnect)) return true;
        if (HandleLightingSet(varKey, value, simConnect)) return true;
        if (HandleIcePitotSet(varKey, value, simConnect)) return true;
        if (HandleEngineStartSet(varKey, value, simConnect)) return true;
        if (HandleEcuSet(varKey, value, simConnect, announcer)) return true;

        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    // ==================================================================================
    // GFC 700 autopilot control types
    //
    // Measured on the aircraft, not assumed: there is NO absolute altitude set.
    //   AP_ALT_VAR_SET_ENGLISH  ignores its parameter and adds +1000 ft (700→1700, 1800→2800)
    //   AP_ALT_VAR_INC / _DEC   ±100 ft
    // so altitude is inherently increment/decrement. Heading and vertical speed follow
    // the same GFC 700 knob model. There is no autothrottle on either airframe
    // (systems.cfg: autothrottle_available = 0), so speed is nominally unused.
    // ==================================================================================

    public override FCUControlType GetAltitudeControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetHeadingControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetSpeedControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.IncrementDecrement;

    // ==================================================================================
    // Visual landing guidance — light GA, not a transport jet
    //
    // The inherited defaults are A320 numbers (Vref 140 kt, ±6° tone range) and are badly
    // wrong for an airframe with a 66–77 kt Vref. Figures below are from the COWS POH
    // p19-20 (NG, 2888 lb). The glideslope bias is the ILS reference datum height only:
    // the DA40's SimConnect datum sits essentially at the GS antenna, so there is no
    // meaningful airframe offset to add on top.
    // ==================================================================================

    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg     = 5.0,
        ReferenceVrefKnots        = IsNG ? 72.0 : 73.0,
        MaxPitchRateDegPerSec     = 5.0,    // light airframe: far more pitch authority
        MaxBankRateDegPerSec      = 8.0,
        TonePitchRangeDeg         = 10.0,   // wider GA attitude envelope than a jet
        ToneBankRangeDeg          = 10.0,
        GlideslopeAltitudeBiasFt  = 50.0    // ICAO TCH only
    };

    // Short wheelbase and a castoring nosewheel — the DA40 pivots far faster than any
    // airliner, so the rollout lead is well below the A320's 1.2 s default.
    public override double TaxiTurnLeadSeconds => 0.8;
}
