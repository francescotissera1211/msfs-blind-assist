using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → ECU (DA40-NG only).
///
/// The AE300 is FADEC-controlled by two redundant ECUs (A and B). The pilot has two
/// controls — the VOTER switch and the ECU TEST button — and the AFM before-takeoff
/// check runs the test once per ECU.
///
/// THE HELD BUTTON. ECU_TEST:1 is declared in the model as ASOBO_GT_Push_Button_Held,
/// and the airframe zeroes it every frame. A single write is therefore DISCARDED — the
/// test never runs. It only works if the value is re-written continuously for the whole
/// press, which is what <see cref="HoldLVar"/> does (~40 ms cadence). Proven live: a
/// 12-second hold produced the real run-up, DISP_PROP_RPM tracing
///
///   700 -> 960 -> 1410 -> 1140 -> 690 -> 980 -> 950 -> 690
///
/// i.e. the propeller cycled twice, once per ECU, exactly as the POH describes, then
/// settled back to idle.
///
/// THE VOTER. L:ECU_VOTER:1 is 0 = ECU B, 1 = AUTO, 2 = ECU A. That ordering is NOT a
/// guess and NOT the obvious A/AUTO/B — it is read from the model's own tooltips
/// (ANIMTIP_0 "ECU B", ANIMTIP_1 "AUTO", ANIMTIP_2 "ECU A"), which are the labels a
/// sighted pilot sees when hovering the switch. All three positions were written and
/// read back live, and L:STATE_VOTER mirrors the value one frame later.
///
/// PRECONDITIONS ARE REPORTED, NEVER ENFORCED. The AFM lists five conditions for the
/// test (power lever idle, voter auto, propeller below 1100 rpm, weight on wheels,
/// gearbox above 35 °C — the checklist recommends 38). All five are readable, so the
/// status display shows each with its live value and the panel can say which one is not
/// met. Nothing here refuses to run the test: a sighted pilot can press the button at
/// any time and so can this one.
///
/// FAILURES. The POH distinguishes unlatched (clears itself) from latched (does not)
/// errors, and the airframe models both: FADEC_ECU_FAIL_A/B against
/// FADEC_ECU_FAIL_LATCH_A/B. Verified live — an ECU A FAIL raised by cycling the engine
/// master would NOT clear via the voter cycle the POH prescribes for unlatched errors,
/// and only the MFD "Reset: ECU" cleared it.
/// </summary>
public partial class CowsDA40Definition
{
    private const string EcuPanel = "ECU";

    /// <summary>AFM: gearbox minimum for the ECU test. The checklist recommends 38.</summary>
    private const double EcuTestGearboxMinC = 38.0;

    /// <summary>AFM: the test needs the propeller below this.</summary>
    private const double EcuTestMaxPropRpm = 1100.0;

    /// <summary>
    /// POH: "Hold the button down until all CAS messages are gone and the engine has
    /// rested at idle. The test takes around 20-25s." Measured with an 11-second hold the
    /// test only reached step 1 of the cycle, so the full duration is what is used here.
    /// </summary>
    private const int EcuTestHoldMs = 26000;

    private static Dictionary<string, SimVarDefinition> BuildEcuVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_ECU_VOTER"] = new SimVarDefinition
        {
            Name = "ECU_VOTER:1",
            DisplayName = "ECU Voter",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            // Order from the model's own ANIMTIPs — not the intuitive A/AUTO/B.
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "ECU B",
                [1] = "Auto",
                [2] = "ECU A"
            },
            HelpText = "Selects which ECU controls the engine. Auto for normal operation. " +
                       "On an ECU failure the AFM has you select the failed ECU, then return " +
                       "to Auto."
        };

        v["DA40_ECU_TEST"] = new SimVarDefinition
        {
            Name = "DA40_ECU_TEST",
            DisplayName = "ECU Test",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false,
            HelpText = "Holds the ECU test button for the full test, about 26 seconds. " +
                       "The propeller " +
                       "cycles once per ECU and the engine returns to idle. Needs the power " +
                       "lever at idle, the voter in Auto, the propeller below 1100 RPM, " +
                       "weight on wheels and the gearbox above 38 degrees."
        };

        // ---------- Status ----------

        v["DA40_ECU_TEST_ACTIVE"] = new SimVarDefinition
        {
            Name = "FADEC_ECUTEST_ACTIVE:1",
            DisplayName = "ECU Test",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not running",
                [1] = "Running"
            }
        };

        AddReadout(v, "DA40_ECU_TEST_STEP", "FADEC_ECUTEST_STEP:1", "ECU Test Step", "", "F0");
        AddReadout(v, "DA40_ECU_TEST_TIMER", "FADEC_ECUTEST_TIMER:1", "ECU Test Timer", "seconds", "F0");

        AddFlag(v, "DA40_ECU_TEST_FAIL_A", "FADEC_ECUTEST_FAIL_A:1", "ECU A Test Result", "Pass", "Fail");
        AddFlag(v, "DA40_ECU_TEST_FAIL_B", "FADEC_ECUTEST_FAIL_B:1", "ECU B Test Result", "Pass", "Fail");
        AddFlag(v, "DA40_ECU_FAIL_A", "FADEC_ECU_FAIL_A:1", "ECU A", "Normal", "Fail");
        AddFlag(v, "DA40_ECU_FAIL_B", "FADEC_ECU_FAIL_B:1", "ECU B", "Normal", "Fail");
        // Latched failures cannot be cleared by the voter cycle — only Reset: ECU on the MFD.
        AddFlag(v, "DA40_ECU_LATCH_A", "FADEC_ECU_FAIL_LATCH_A:1", "ECU A Fault Latched", "No", "Yes, latched");
        AddFlag(v, "DA40_ECU_LATCH_B", "FADEC_ECU_FAIL_LATCH_B:1", "ECU B Fault Latched", "No", "Yes, latched");

        AddReadout(v, "DA40_ECU_RUNTIME_A", "STATE_ECU_A_RUNTIME:1", "ECU A Runtime", "hours", "F1");
        AddReadout(v, "DA40_ECU_RUNTIME_B", "STATE_ECU_B_RUNTIME:1", "ECU B Runtime", "hours", "F1");

        // The five AFM preconditions, each shown with its live value so the pilot can see
        // which one is short rather than being told "not ready".
        AddReadout(v, "DA40_ECU_PRE_GEARBOX", "DISP_GT", "Gearbox Temperature", "celsius", "F0");
        AddReadout(v, "DA40_ECU_PRE_PROP_RPM", "DISP_PROP_RPM", "Propeller RPM", "rpm", "F0");
        AddReadout(v, "DA40_ECU_PRE_POWER_LEVER", "FADEC_POWER_LEVER:1", "Power Lever", "percent", "F0");

        v["DA40_ECU_PRE_ON_GROUND"] = new SimVarDefinition
        {
            Name = "SIM ON GROUND",
            DisplayName = "Weight On Wheels",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Airborne", [1] = "On ground" }
        };

        return v;
    }

    /// <summary>Read-only two-state flag readout.</summary>
    private static void AddFlag(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display, string offText, string onText)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = offText, [1] = onText }
        };
    }

    private static readonly List<string> EcuControls = new()
    {
        "DA40_ECU_VOTER",
        "DA40_ECU_TEST"
    };

    private static readonly List<string> EcuDisplay = new()
    {
        "DA40_ECU_TEST_ACTIVE",
        "DA40_ECU_TEST_STEP",
        "DA40_ECU_TEST_TIMER",
        "DA40_ECU_TEST_FAIL_A",
        "DA40_ECU_TEST_FAIL_B",
        "DA40_ECU_FAIL_A",
        "DA40_ECU_FAIL_B",
        "DA40_ECU_LATCH_A",
        "DA40_ECU_LATCH_B",
        "DA40_ECU_RUNTIME_A",
        "DA40_ECU_RUNTIME_B",
        // Preconditions last — the pilot reads down to them when the test will not run.
        "DA40_ECU_PRE_POWER_LEVER",
        "DA40_ECU_PRE_PROP_RPM",
        "DA40_ECU_PRE_GEARBOX",
        "DA40_ECU_PRE_ON_GROUND"
    };

    // ==================================================================================
    // Held L-var writer
    // ==================================================================================

    private System.Windows.Forms.Timer? _holdTimer;
    private string _holdVar = "";
    private long _holdUntilTicks;

    /// <summary>
    /// Holds an L:var at 1 for <paramref name="holdMs"/> by re-writing it every ~40 ms,
    /// then releases it to 0.
    ///
    /// This exists because a whole class of COWS DA40 controls (ECU_TEST:1, ATT_CAGE,
    /// FUEL_SELECTOR_WIRE_CUT, the trim buttons) are declared as *_Held / momentary in
    /// the model and are ZEROED BY THE AIRFRAME EVERY FRAME. A single SetLVar is
    /// discarded before the model ever sees a press, so the existing press/release
    /// helpers — which send two H: events, or write once and wait — cannot drive them.
    /// Measured: ATT_CAGE written once read back 0; re-written every 40 ms it held at 1
    /// and drove ATT_GYRO_CAGE_SET 0 to 1.
    ///
    /// One hold at a time: a second call releases the first, so a stray double-press
    /// cannot leave a control stuck down.
    /// </summary>
    private void HoldLVar(string lvar, int holdMs, SimConnectManager simConnect)
    {
        ReleaseHeldLVar(simConnect);

        _holdVar = lvar;
        _holdUntilTicks = Environment.TickCount64 + holdMs;

        simConnect.SetLVar(lvar, 1);

        _holdTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _holdTimer.Tick += (_, _) =>
        {
            try
            {
                if (Environment.TickCount64 >= _holdUntilTicks || !simConnect.IsConnected)
                {
                    ReleaseHeldLVar(simConnect);
                    return;
                }
                simConnect.SetLVar(_holdVar, 1);
            }
            catch (Exception ex)
            {
                Log.Debug("DA40", $"Held-write tick failed for {_holdVar}: {ex.Message}");
                ReleaseHeldLVar(simConnect);
            }
        };
        _holdTimer.Start();

        Log.Debug("DA40", $"Holding L:{lvar} for {holdMs} ms");
    }

    private void ReleaseHeldLVar(SimConnectManager simConnect)
    {
        if (_holdTimer == null) return;

        _holdTimer.Stop();
        _holdTimer.Dispose();
        _holdTimer = null;

        if (!string.IsNullOrEmpty(_holdVar))
        {
            try { simConnect.SetLVar(_holdVar, 0); } catch { /* release must never throw */ }
            Log.Debug("DA40", $"Released L:{_holdVar}");
            _holdVar = "";
        }
    }

    /// <summary>
    /// Reports which ECU test preconditions are not met, in the AFM's own terms and with
    /// the live value that fails. Returns an empty list when the test should run.
    /// This is spoken; it never blocks the button.
    /// </summary>
    private static List<string> EcuTestBlockers(SimConnectManager simConnect)
    {
        var blockers = new List<string>();

        double Lv(string n) => simConnect.GetCachedVariableValue(n) ?? 0;

        double powerLever = Lv("DA40_ECU_PRE_POWER_LEVER");
        double propRpm = Lv("DA40_ECU_PRE_PROP_RPM");
        double gearbox = Lv("DA40_ECU_PRE_GEARBOX");
        double voter = Lv("DA40_ECU_VOTER");
        double onGround = Lv("DA40_ECU_PRE_ON_GROUND");

        if (powerLever > 1) blockers.Add($"power lever {powerLever:0} percent, not idle");
        if (voter != 1) blockers.Add("voter not in auto");
        if (propRpm >= EcuTestMaxPropRpm) blockers.Add($"propeller {propRpm:0} RPM, needs below {EcuTestMaxPropRpm:0}");
        if (onGround < 0.5) blockers.Add("not on the ground");
        if (gearbox < EcuTestGearboxMinC) blockers.Add($"gearbox {gearbox:0} degrees, needs {EcuTestGearboxMinC:0}");

        return blockers;
    }

    private bool HandleEcuSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_ECU_VOTER":
                simConnect.SetLVar("ECU_VOTER:1", value);
                return true;

            case "DA40_ECU_TEST":
            {
                // Say what is not met, then run it anyway — the pilot decides.
                var blockers = EcuTestBlockers(simConnect);
                announcer.AnnounceImmediate(blockers.Count == 0
                    ? "ECU test running."
                    : "ECU test running. " + string.Join(", ", blockers) + ".");

                HoldLVar("ECU_TEST:1", EcuTestHoldMs, simConnect);
                return true;
            }
        }

        return false;
    }
}
