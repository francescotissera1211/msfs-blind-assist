using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Engine Start (DA40-NG).
///
/// Built against AFM 4A.5.3 STARTING ENGINE and verified by a full shutdown-and-restart
/// cycle on the live aircraft. The measured trace:
///
///   before        starter=0 comb=1 rpm=1188 oilPSI=34 key=1
///   MASTER 0   -> starter=0 comb=0 rpm=0    oilPSI=7  key=1     (shut down)
///   MASTER 1   -> starter=0 comb=0 rpm=0    oilPSI=0  key=1
///   STARTER 1  -> starter=1 comb=1 rpm=839  oilPSI=11 key=2     (cranking)
///   +1s        -> starter=0 comb=1 rpm=1175 oilPSI=29 key=1     (auto-released)
///   settled    -> starter=0 comb=1 rpm=1185 oilPSI=34 key=1
///
/// WHAT THE AIRFRAME DOES THAT THE VARIABLE LIST DOES NOT SHOW:
///
///  • L:STARTER_SWITCH is the KEY POSITION and is READ-ONLY (0 OFF / 1 ON / 2 START).
///    It is computed from ELECTRICAL MASTER BATTERY and GENERAL ENG STARTER; writing it
///    does nothing. It is offered here purely as a status readout, and it genuinely
///    reads 2 while cranking.
///  • The write path is K:SET_STARTER1_HELD — 1 engages, 0 releases. The model
///    AUTO-RELEASES once the engine catches (observed at t+1s), which mirrors the real
///    spring-loaded key being let go.
///  • Glow plugs are L:GLOW_ON:1 with L:START_GLOW_TIMER:1 / L:START_GLOW_TEMP:1.
///    AFM: "GLOW ON is indicated only when the engine is cold", so a warm-engine start
///    shows it off — that is correct, not a dead variable.
///
/// TWO AFM LIMITS ARE SURFACED AS INFORMATION, NEVER AS A GATE (see §0.4 of the
/// architecture notes — MSFSBA reports, the pilot decides):
///   - Starter motor: max 10 seconds continuous, 60 s between attempts.
///   - Oil pressure must leave the red range within 3 seconds of starting.
/// Both live in HelpText and in the status display's elapsed-crank readout. Nothing
/// here stops the pilot cranking for as long as they like.
///
/// The Engine Master lives HERE and nowhere else. The AFM's instrument-panel legend
/// groups it with the engine controls (7 ECU Test, 8 ECU Voter, 9 Engine Master), and
/// AFM start item 2 is ENGINE MASTER — it is an engine control that happens to be a
/// master switch. No control is duplicated across panels.
///
/// GUARDS ARE STATE, NOT INTERLOCKS — measured. With MASTER_COVER:1 forced to 0,
/// K:ENGINE_MASTER_1_SET still moved the switch and shut the engine down, and the model
/// then AUTO-OPENED the cover. The emergency-battery guard does not even do that. So the
/// guard is exposed as its own control (the pilot can work it exactly as in the real
/// cockpit) but MSFSBA must not pretend it gates anything, and must not invent a gate of
/// its own — report, do not decide.
///
/// NO SOUND ON PROGRAMMATIC WRITES. The cockpit WWISE events
/// (deice_cover_alternate_switch_on, starter_push_button_on, ...) are attached to the
/// model's CLICKSPOT templates, so an L:var or K: event write bypasses them. Silence
/// from a panel control is expected and is not a failed write — verify by readback.
/// </summary>
public partial class CowsDA40Definition
{
    private const string EngineStartPanel = "Engine Start";

    private static Dictionary<string, SimVarDefinition> BuildEngineStartVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        // Red-guarded on the real aircraft. AFM: "Lift the guard prior to actuating the
        // toggle. After switching, lower the Engine Master switch guard with the toggle
        // in the desired position."
        v["DA40_START_ENGINE_MASTER"] = new SimVarDefinition
        {
            Name = "GENERAL ENG MASTER ALTERNATOR:1",
            DisplayName = "Engine Master",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
            HelpText = "Guarded switch. Turning it off shuts the engine down."
        };

        v["DA40_START_ENGINE_MASTER_COVER"] = new SimVarDefinition
        {
            Name = "MASTER_COVER:1",
            DisplayName = "Engine Master Guard",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" },
            HelpText = "The guard does not block the switch in the simulator, and the " +
                       "model opens it by itself when the master is operated."
        };

        v["DA40_START_STARTER_ENGAGE"] = new SimVarDefinition
        {
            Name = "DA40_START_STARTER_ENGAGE",
            DisplayName = "Start Key",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false,
            HelpText = "Turns the key to START and holds it. The starter releases itself " +
                       "once the engine catches. AFM limit: do not crank for more than 10 " +
                       "seconds, and wait 60 seconds between attempts."
        };

        v["DA40_START_STARTER_RELEASE"] = new SimVarDefinition
        {
            Name = "DA40_START_STARTER_RELEASE",
            DisplayName = "Release Start Key",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false,
            HelpText = "Releases the start key back to ON. Only needed if the engine did " +
                       "not catch — the key releases itself on a successful start."
        };

        // ---------- Status ----------

        v["DA40_START_KEY_POSITION"] = new SimVarDefinition
        {
            Name = "STARTER_SWITCH",
            DisplayName = "Key Position",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On",
                [2] = "Start"
            }
        };

        v["DA40_START_STARTER_ENGAGED"] = new SimVarDefinition
        {
            Name = "GENERAL ENG STARTER:1",
            DisplayName = "Starter",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Disengaged",
                [1] = "Engaged"
            }
        };

        v["DA40_START_GLOW_ON"] = new SimVarDefinition
        {
            Name = "GLOW_ON:1",
            DisplayName = "Glow Plugs",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On, wait"
            },
            HelpText = "Glow plugs preheat a cold engine. Wait for this to go off before " +
                       "cranking. A warm engine never shows it."
        };

        v["DA40_START_COMBUSTION"] = new SimVarDefinition
        {
            Name = "GENERAL ENG COMBUSTION:1",
            DisplayName = "Engine Running",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Stopped",
                [1] = "Running"
            }
        };

        AddReadout(v, "DA40_START_GLOW_TIMER", "START_GLOW_TIMER:1", "Glow Timer", "seconds", "F0");
        AddReadout(v, "DA40_START_GLOW_TEMP", "START_GLOW_TEMP:1", "Glow Plug Temperature", "celsius", "F0");

        // Idle target from AFM start item 10: 710 +/- 30 RPM (higher above 7000 ft).
        //
        // Read from PROP_RPM_SENS:1, not DISP_PROP_RPM. The EIS variable is QUANTISED TO
        // 10 RPM — measured 710 while the true speed was 705.12 — because that is how the
        // G1000 renders the digits. But the gauge's needle moves smoothly, so a sighted
        // pilot sees continuous change, and rounding to 10 would hide it. The sensed value
        // gives 1 RPM resolution, which is what the needle conveys.
        AddReadout(v, "DA40_START_RPM", "PROP_RPM_SENS:1", "Propeller RPM", "rpm", "F0");
        // AFM warning: must leave the red range within 3 seconds of starting.
        AddReadout(v, "DA40_START_OIL_PRESSURE", "DISP_OP", "Oil Pressure", "bar", "F2");
        AddReadout(v, "DA40_START_OIL_TEMP", "DISP_OT", "Oil Temperature", "celsius", "F0");
        // ECU test needs 38 C; also the gate on whether the engine is warm enough to work.
        AddReadout(v, "DA40_START_GEARBOX_TEMP", "DISP_GT", "Gearbox Temperature", "celsius", "F0");
        AddReadout(v, "DA40_START_COOLANT_TEMP", "DISP_WT", "Coolant Temperature", "celsius", "F0");
        AddReadout(v, "DA40_START_VOLTS", "DISP_VOLTS", "Volts", "volts", "F1");
        AddReadout(v, "DA40_START_LOAD", "DISP_LD", "Engine Load", "percent", "F0");

        return v;
    }

    private static readonly List<string> EngineStartControls = new()
    {
        "DA40_START_ENGINE_MASTER_COVER",
        "DA40_START_ENGINE_MASTER",
        "DA40_START_STARTER_ENGAGE",
        "DA40_START_STARTER_RELEASE"
    };

    // The scan a pilot runs during a start, in the order the AFM checks them:
    // is it primed, is it turning, is it running, is the oil up, is it at idle.
    private static readonly List<string> EngineStartDisplay = new()
    {
        "DA40_START_KEY_POSITION",
        "DA40_START_GLOW_ON",
        "DA40_START_GLOW_TIMER",
        "DA40_START_GLOW_TEMP",
        "DA40_START_STARTER_ENGAGED",
        "DA40_START_COMBUSTION",
        "DA40_START_RPM",
        "DA40_START_OIL_PRESSURE",
        "DA40_START_OIL_TEMP",
        "DA40_START_COOLANT_TEMP",
        "DA40_START_GEARBOX_TEMP",
        "DA40_START_LOAD",
        "DA40_START_VOLTS"
    };

    /// <summary>
    /// Engine Start writes. The two buttons drive K:SET_STARTER1_HELD directly — 1 to
    /// turn the key to START, 0 to let it back. No timer, no auto-release and no crank
    /// limit is imposed here: the AFM's 10-second limit is information the pilot acts on,
    /// not something MSFSBA enforces.
    /// </summary>
    private bool HandleEngineStartSet(string varKey, double value, SimConnectManager simConnect)
    {
        switch (varKey)
        {
            case "DA40_START_ENGINE_MASTER":
                simConnect.ExecuteCalculatorCode($"{(value >= 0.5 ? 1 : 0)} (>K:ENGINE_MASTER_1_SET)");
                return true;

            case "DA40_START_ENGINE_MASTER_COVER":
                simConnect.SetLVar("MASTER_COVER:1", value >= 0.5 ? 1 : 0);
                return true;

            case "DA40_START_STARTER_ENGAGE":
                simConnect.ExecuteCalculatorCode("1 (>K:SET_STARTER1_HELD)");
                return true;

            case "DA40_START_STARTER_RELEASE":
                simConnect.ExecuteCalculatorCode("0 (>K:SET_STARTER1_HELD)");
                return true;
        }

        return false;
    }
}
