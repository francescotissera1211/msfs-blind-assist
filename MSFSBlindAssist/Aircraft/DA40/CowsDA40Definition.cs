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
public class CowsDA40Definition : BaseAircraftDefinition
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
            ["Circuit Breakers"] = new List<string>
            {
                "CB Engine and Fuel",
                "CB Flight Instruments",
                "CB Avionics",
                "CB Electrical",
                "CB Lighting",
                "CB Systems",
                "CB Copilot"
            },
            ["G1000 PFD"] = new List<string>
            {
                "PFD Softkeys",
                "PFD Bezel",
                "PFD Readout",
                "CAS Messages"
            },
            ["G1000 MFD"] = new List<string>
            {
                "MFD Softkeys",
                "MFD Bezel",
                "Engine Indication",
                "Fuel Calculator",
                "Aircraft Options",
                "Electronic Checklist"
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
            structure["G1000 MFD"].Insert(3, "Lean Assist");
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

        return controls;
    }

    protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
    {
        // Base variables only for now (position, speeds, the shared airframe set).
        // Aircraft-specific variables arrive with their panels.
        return GetBaseVariables();
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
        => new Dictionary<string, List<string>>();

    public override Dictionary<string, string> GetButtonStateMapping()
        => new Dictionary<string, string>();

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
