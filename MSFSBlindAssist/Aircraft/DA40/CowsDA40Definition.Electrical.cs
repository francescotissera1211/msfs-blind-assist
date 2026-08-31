using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Electrical.
///
/// Every switch and every readout below was exercised against a powered DA40-NG
/// (2026-08): each control was written, read back, and confirmed by a DOWNSTREAM
/// effect (a bus voltage moving, the alternator loading up), not merely by the
/// switch variable echoing itself. The aircraft was deliberately taken to full dark
/// and restarted to prove the cold states.
///
/// FOUR THINGS THE OBVIOUS DESIGN GETS WRONG HERE — all measured:
///
///  1. There is NO alternator switch. The AE300's alternator is ECU-controlled.
///     L:STATE_ALTERNATOR reads 0 while the alternator is happily producing 28 A,
///     so it is NOT an on/off state and must never be offered as a toggle.
///
///  2. L:STARTER_SWITCH is a DERIVED MIRROR, not an input. Writing 1 to it reads
///     back 0: the model computes the key position from ELECTRICAL MASTER BATTERY
///     and GENERAL ENG STARTER. The key is operated through those events instead.
///
///  3. The avionics master is index 1, NOT 2 — despite the model XML declaring
///     AVIONICS_BUS_ID 2. K:AVIONICS_MASTER_2_SET does nothing at all;
///     K:TOGGLE_AVIONICS_MASTER moves A:AVIONICS MASTER SWITCH:1.
///
///  4. The engine master reads back on A:GENERAL ENG MASTER ALTERNATOR:1 (the
///     ASOBO_ENGINE_Switch_Master_Template mapping), not on any L:var.
///
/// Writes use a CONDITIONAL TOGGLE for the two switches that only expose a toggle
/// event: comparing the current state first makes a combo idempotent, so picking
/// "On" when it is already on cannot flip it off.
/// </summary>
public partial class CowsDA40Definition
{
    // Panel key — must match the name used in GetPanelStructure().
    private const string ElectricalPanel = "Electrical";

    // ==================================================================================
    // Variables
    // ==================================================================================

    private static Dictionary<string, SimVarDefinition> BuildElectricalVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Switches ----------

        // The ignition key's OFF/ON detents ARE the battery master (model XML:
        // CODE_POS_0 turns it off, CODE_POS_1 turns it on). Written via a conditional
        // TOGGLE_MASTER_BATTERY; read from the standard SimVar.
        v["DA40_ELEC_MASTER_BATTERY"] = new SimVarDefinition
        {
            Name = "ELECTRICAL MASTER BATTERY:1",
            DisplayName = "Electric Master",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

        v["DA40_ELEC_AVIONICS_MASTER"] = new SimVarDefinition
        {
            Name = "AVIONICS MASTER SWITCH:1",
            DisplayName = "Avionics Master",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

        // Red-guarded. The guard is a separate animation variable; opening it is a
        // real action the pilot performs, so it gets its own control rather than being
        // silently automated away.
        v["DA40_ELEC_ENGINE_MASTER"] = new SimVarDefinition
        {
            Name = "GENERAL ENG MASTER ALTERNATOR:1",
            DisplayName = "Engine Master",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

        v["DA40_ELEC_ENGINE_MASTER_COVER"] = new SimVarDefinition
        {
            Name = "MASTER_COVER:1",
            DisplayName = "Engine Master Guard",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        };

        // Measured: setting this to 1 drops ELEC_BUS_MAIN_VOLT to 0 while the ESS and
        // emergency buses stay live — it isolates the aircraft to the essential bus.
        v["DA40_ELEC_ESS_BUS"] = new SimVarDefinition
        {
            Name = "ESS_BUS_SWITCH",
            DisplayName = "Essential Bus",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Essential bus only" }
        };

        v["DA40_ELEC_EMER_BATT_COVER"] = new SimVarDefinition
        {
            Name = "EMERGENCY_BATT_COVER",
            DisplayName = "Emergency Battery Guard",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        };

        // Measured: 0→1 puts 30 V on ELEC_BUS_EMER_VOLT from the 20 Ah emergency pack.
        v["DA40_ELEC_EMER_BATT"] = new SimVarDefinition
        {
            Name = "EMERGENCY_BATT",
            DisplayName = "Emergency Battery",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

        // ---------- Read-only bus and battery state ----------
        // Everything the EIS electrical strip shows, plus the per-bus detail a sighted
        // pilot infers from which instruments are alive. Raw values, per bus — never a
        // single "electrical OK".

        AddReadout(v, "DA40_ELEC_BUS_MAIN_VOLT", "ELEC_BUS_MAIN_VOLT", "Main Bus Volts", "volts", "F1");
        AddReadout(v, "DA40_ELEC_BUS_MAIN_AMPS", "ELEC_BUS_MAIN_AMPS", "Main Bus Amps", "amperes", "F1");
        AddReadout(v, "DA40_ELEC_BUS_ESS_VOLT", "ELEC_BUS_ESS_VOLT", "Essential Bus Volts", "volts", "F1");
        AddReadout(v, "DA40_ELEC_BUS_ESS_AMPS", "ELEC_BUS_ESS_AMPS", "Essential Bus Amps", "amperes", "F1");
        AddReadout(v, "DA40_ELEC_BUS_EMER_VOLT", "ELEC_BUS_EMER_VOLT", "Emergency Bus Volts", "volts", "F1");
        AddReadout(v, "DA40_ELEC_BUS_BATT_VOLT", "ELEC_BUS_BATT_VOLT", "Battery Bus Volts", "volts", "F1");
        // Stays live with the master off — it is the permanently hot bus.
        AddReadout(v, "DA40_ELEC_BUS_HOT_VOLT", "ELEC_BUS_HOT_VOLT", "Hot Battery Bus Volts", "volts", "F1");
        AddReadout(v, "DA40_ELEC_BUS_ECU1_VOLT", "ELEC_BUS_ECU1_VOLT", "ECU Bus Volts", "volts", "F1");

        AddReadout(v, "DA40_ELEC_BATT_VOLT", "ELEC_BATT_VOLT", "Main Battery Volts", "volts", "F1");
        // Negative = discharging, positive = charging. Measured -17.6 A on battery only.
        AddReadout(v, "DA40_ELEC_BATT_AMPS", "ELEC_BATT_AMPS", "Main Battery Amps", "amperes", "F1");
        AddReadout(v, "DA40_ELEC_BATT_PERCENT", "ELEC_BATT_PERCENT", "Main Battery Charge", "percent", "F0");
        AddReadout(v, "DA40_ELEC_BATT_TEMP", "ELEC_BATT_TEMP", "Main Battery Temperature", "celsius", "F0");
        // Amp-hours remaining, NOT a boolean — STATE_BATT mirrors this same figure.
        AddReadout(v, "DA40_ELEC_BATT_CAPACITY", "ELEC_BATT_CAPACITY", "Main Battery Capacity", "amp hours", "F0");

        AddReadout(v, "DA40_ELEC_BATT_ECU_VOLT", "ELEC_BATT_ECU_VOLT", "ECU Battery Volts", "volts", "F1");
        AddReadout(v, "DA40_ELEC_BATT_ECU_PERCENT", "ELEC_BATT_ECU_PERCENT", "ECU Battery Charge", "percent", "F0");
        AddReadout(v, "DA40_ELEC_BATT_EMER_VOLT", "ELEC_BATT_EMER_VOLT", "Emergency Battery Volts", "volts", "F1");
        AddReadout(v, "DA40_ELEC_BATT_EMER_CAPACITY", "ELEC_BATT_EMER_CAPACITY", "Emergency Battery Capacity", "amp hours", "F0");

        AddReadout(v, "DA40_ELEC_ALT_VOLT", "ELEC_ALT_VOLT_OUT", "Alternator Volts", "volts", "F1");
        AddReadout(v, "DA40_ELEC_ALT_AMPS", "ELEC_ALT_AMPS", "Alternator Amps", "amperes", "F1");
        AddReadout(v, "DA40_ELEC_ALT_AMPS_MAX", "ELEC_ALT_AMPS_MAX", "Alternator Amps Available", "amperes", "F1");

        // The two figures the G1000 electrical strip actually shows.
        AddReadout(v, "DA40_ELEC_DISP_VOLTS", "DISP_VOLTS", "Indicated Volts", "volts", "F1");
        AddReadout(v, "DA40_ELEC_DISP_AMPS", "DISP_AMPS", "Indicated Amps", "amperes", "F1");

        return v;
    }

    /// <summary>Read-only numeric L:var readout for a status display.</summary>
    private static void AddReadout(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display, string units, string format)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = units,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = format
        };
    }

    // Interactive controls, in the order a pilot works down the panel.
    private static readonly List<string> ElectricalControls = new()
    {
        "DA40_ELEC_MASTER_BATTERY",
        "DA40_ELEC_AVIONICS_MASTER",
        "DA40_ELEC_ENGINE_MASTER_COVER",
        "DA40_ELEC_ENGINE_MASTER",
        "DA40_ELEC_ESS_BUS",
        "DA40_ELEC_EMER_BATT_COVER",
        "DA40_ELEC_EMER_BATT"
    };

    // The Ctrl+3 scan: what a sighted pilot's glance at the electrical strip takes in,
    // in a deliberate order — what the aircraft is running on, then each bus, then the
    // batteries, then the alternator.
    private static readonly List<string> ElectricalDisplay = new()
    {
        "DA40_ELEC_DISP_VOLTS",
        "DA40_ELEC_DISP_AMPS",
        "DA40_ELEC_BUS_MAIN_VOLT",
        "DA40_ELEC_BUS_MAIN_AMPS",
        "DA40_ELEC_BUS_ESS_VOLT",
        "DA40_ELEC_BUS_ESS_AMPS",
        "DA40_ELEC_BUS_EMER_VOLT",
        "DA40_ELEC_BUS_BATT_VOLT",
        "DA40_ELEC_BUS_HOT_VOLT",
        "DA40_ELEC_BUS_ECU1_VOLT",
        "DA40_ELEC_BATT_VOLT",
        "DA40_ELEC_BATT_AMPS",
        "DA40_ELEC_BATT_PERCENT",
        "DA40_ELEC_BATT_TEMP",
        "DA40_ELEC_BATT_CAPACITY",
        "DA40_ELEC_BATT_ECU_VOLT",
        "DA40_ELEC_BATT_ECU_PERCENT",
        "DA40_ELEC_BATT_EMER_VOLT",
        "DA40_ELEC_BATT_EMER_CAPACITY",
        "DA40_ELEC_ALT_VOLT",
        "DA40_ELEC_ALT_AMPS",
        "DA40_ELEC_ALT_AMPS_MAX"
    };

    // ==================================================================================
    // Writes
    // ==================================================================================

    /// <summary>
    /// Routes the Electrical panel's writes. Returns false for anything else so the
    /// generic path keeps handling it.
    ///
    /// The battery and avionics masters expose only a TOGGLE event, so a naive combo
    /// would flip them the wrong way whenever the picked value already matched. Both
    /// are written as a conditional toggle in RPN — read, compare, toggle only if
    /// different — which makes the combo idempotent.
    /// </summary>
    private bool HandleElectricalSet(string varKey, double value, SimConnectManager simConnect)
    {
        bool on = value >= 0.5;

        switch (varKey)
        {
            case "DA40_ELEC_MASTER_BATTERY":
                simConnect.ExecuteCalculatorCode(
                    $"(A:ELECTRICAL MASTER BATTERY:1, Bool) {(on ? 0 : 1)} == " +
                    "if{ 1 (>K:TOGGLE_MASTER_BATTERY) }");
                return true;

            case "DA40_ELEC_AVIONICS_MASTER":
                // Index 1, not 2 — AVIONICS_MASTER_2_SET is inert on this aircraft.
                simConnect.ExecuteCalculatorCode(
                    $"(A:AVIONICS MASTER SWITCH:1, Bool) {(on ? 0 : 1)} == " +
                    "if{ 1 (>K:TOGGLE_AVIONICS_MASTER) }");
                return true;

            case "DA40_ELEC_ENGINE_MASTER":
                // This one has a real SET event, so no conditional dance is needed.
                simConnect.ExecuteCalculatorCode($"{(on ? 1 : 0)} (>K:ENGINE_MASTER_1_SET)");
                return true;

            case "DA40_ELEC_ENGINE_MASTER_COVER":
                simConnect.SetLVar("MASTER_COVER:1", on ? 1 : 0);
                return true;

            case "DA40_ELEC_ESS_BUS":
                simConnect.SetLVar("ESS_BUS_SWITCH", on ? 1 : 0);
                return true;

            case "DA40_ELEC_EMER_BATT_COVER":
                simConnect.SetLVar("EMERGENCY_BATT_COVER", on ? 1 : 0);
                return true;

            case "DA40_ELEC_EMER_BATT":
                simConnect.SetLVar("EMERGENCY_BATT", on ? 1 : 0);
                return true;
        }

        return false;
    }
}
