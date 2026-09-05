using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Standby Instruments.
///
/// AFM legend items 17-20: the backup airspeed indicator, backup artificial horizon,
/// backup altimeter and the emergency compass. Only two of those are adjustable — the
/// altimeter's subscale and the horizon's cage knob — so they are the controls, and the
/// rest of the standby panel is reported.
///
/// THE GYRO IS PROPERLY MODELLED, which is why it gets more than an on/off readout. The
/// airframe simulates spin-up and topple: ATT_GYRO_SPEED runs 0 to 1 as the rotor comes
/// up (measured 0.936 on a running engine), ATT_GYRO_RIGID is its rigidity, and
/// ATT_GYRO_TOPPLE rises when it is tumbled. A vacuum-less electric standby horizon that
/// has toppled reads a lie, and a blind pilot has no other way to notice, so all three
/// are on the scan next to the attitude it is showing.
///
/// The cage knob is a HELD control (ASOBO_GT_Push_Button_Held on ATT_CAGE). The airframe
/// zeroes ATT_CAGE every frame, so a single write is discarded — this was the first
/// control that proved the point: written once it read back 0, but re-written every 40 ms
/// it held at 1 and drove ATT_GYRO_CAGE_SET from 0 to 1. It therefore goes through
/// HoldLVar like the ECU test.
///
/// The backup altimeter has its OWN subscale, L:KOHLSMAN SETTING HG:2, stepping 0.01 inHg
/// between 28.00 and 31.50 — a separate setting from the G1000's, which is exactly why
/// the AFM's descent checklist says "Altimeters (2) ... SET". Both must be set.
///
/// The Display Backup button lives here rather than with the audio panel it is physically
/// mounted on: reversionary mode is what a pilot reaches for when a display dies, which is
/// the same emergency the standby instruments exist for.
/// </summary>
public partial class CowsDA40Definition
{
    private const string StandbyPanel = "Standby Instruments";

    /// <summary>
    /// Kept SHORT. The held writer re-writes at 40 ms and the airframe plays the knob
    /// click on every write, so a long hold is audible as a burst of clicking.
    /// ATT_GYRO_CAGE_SET was observed set within about 400 ms, so this is enough.
    /// </summary>
    private const int GyroCageHoldMs = 700;

    private static Dictionary<string, SimVarDefinition> BuildStandbyVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_STBY_ALTIMETER_SET"] = new SimVarDefinition
        {
            // READS THE MIRROR, WRITES THE INPUT - this aeroplane's own rule, and the
            // one place it was not being followed.
            //
            // The subscale's INPUT is "L:KOHLSMAN SETTING HG:2", whose name carries a
            // space AND a colon. That shape is normally a stock SimVar, so it defeats both
            // of MSFSBA's normal paths at once: SetLVar refuses the calculator for it and
            // falls back to a data-def write that lands on the STOCK SimVar of that name
            // (a different variable - measured, the L:var moved to 30.11 while the SimVar
            // stayed at 29.85), and the data-def READ asked for it in "inHg", which makes
            // SimConnect convert a raw number from its base pressure unit. The pilot heard
            // "Standby 0 hectopascals, 0.01 inches" over a subscale that was set correctly.
            //
            // L:STATE_BARO2 is the airframe's own read-only mirror of the same subscale -
            // measured at 30.11 alongside the input - and its name is CLEAN, so it reads
            // through the ordinary path with no special case at all. The write goes to the
            // input through SetStandbyBaro.
            Name = "STATE_BARO2",
            DisplayName = "Standby Altimeter Setting",
            Type = SimVarType.LVar,
            // "number", never "inHg": an L:var holds a raw number and a pressure unit here
            // makes SimConnect convert it. Same trap as the A380's TCAS vertical-speed
            // L:vars, which must be "number" and not "feet per minute".
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            // ⚠️ SIM_FRAME, NOT THE 1 Hz BATCH - a subscale the pilot is TURNING moves faster
            // than the batch samples it, which is what forced the settle to outlast a batch
            // period and left every read-back a beat behind. Same reasoning as the radio
            // frequencies and as G_FORCE's touchdown spike; CHANGED means a still altimeter
            // costs nothing.
            ExcludeFromBatch = true,
            HighFrequency = true,
            // A TEXT FIELD, not a slider. MainForm's TrackBar is hardcoded 0-100 and maps
            // the value as a PERCENTAGE of the slider range — right for a lighting knob,
            // but it reported this subscale as "0 to 100" instead of 28 to 31.5. The key
            // ends in _SET, so dropping RenderAsSlider gives a typed entry instead.
            Format = "F2",
            HelpText = "Separate from the G1000 subscale - set both. Takes hectopascals or inches."
        };

        v["DA40_STBY_GYRO_CAGE"] = new SimVarDefinition
        {
            Name = "DA40_STBY_GYRO_CAGE",
            DisplayName = "Cage Attitude Indicator",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false,
            HelpText = "Re-erects the backup horizon. Cage it level."
        };

        v["DA40_STBY_DISPLAY_BACKUP"] = new SimVarDefinition
        {
            Name = "G1000_REV_FORCE",
            DisplayName = "Display Backup",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Normal",
                [1] = "Reversionary"
            },
            HelpText = "Reversionary mode after a display failure."
        };

        // ---------- Status ----------

        // No separate "Standby Subscale" readout: the setting control above now reads
        // STATE_BARO2 itself, so a second row would report the same number twice under two
        // names - the duplication this aeroplane's panels are explicitly meant not to have.

        AddFlag(v, "DA40_STBY_GYRO_CAGED", "ATT_GYRO_CAGE_SET", "Gyro Caged", "No", "Yes");

        // The standby horizon's own attitude, which is the whole point of having it.
        // What the standby horizon is SHOWING - the instrument's own indication, not the
        // aeroplane's attitude. They drift apart: measured 2.2 degrees indicated against a
        // true -3.0, a five-degree error, on a gyro that had been running a while.
        AddReadout(v, "DA40_STBY_GYRO_PITCH", "ATT_GYRO_REL_PITCH", "Standby Horizon Pitch", "degrees", "F1");
        AddReadout(v, "DA40_STBY_GYRO_BANK", "ATT_GYRO_REL_BANK", "Standby Horizon Bank", "degrees", "F1");

        // Spin-up and topple. A toppled gyro reads a plausible lie; without these there is
        // no way to know the instrument has stopped being trustworthy.
        // The TRUE attitude, so the drift above is visible rather than implied. A sighted
        // pilot spots a leaning standby horizon by comparing it against the PFD; this is
        // that comparison, in numbers.
        AddTrueAttitude(v, "DA40_STBY_TRUE_PITCH", "ATTITUDE INDICATOR PITCH DEGREES", "Actual Pitch");
        AddTrueAttitude(v, "DA40_STBY_TRUE_BANK", "ATTITUDE INDICATOR BANK DEGREES", "Actual Bank");

        // Rotor speed as a percentage. The model spins it 0 to 1 from the EMERGENCY bus
        // (ELEC_BUS_EMER_VOLT / 30), which is why the standby horizon keeps working when
        // the main bus is dead. Below about 10 percent it is not usable.
        v["DA40_STBY_GYRO_SPEED"] = new SimVarDefinition
        {
            Name = "ATT_GYRO_SPEED",
            DisplayName = "Gyro Spin",
            Type = SimVarType.LVar,
            // "number", with Scale doing the 0-1 to percent conversion below. Asking a
            // data definition for an L:var in "percent" invites SimConnect to convert it
            // as well, which would scale the same number twice - the trap that read the
            // standby subscale as zero when it was asked for in inHg.
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Scale = 100.0,
            Format = "F0"
        };

        // Toppled means the instrument has tumbled and is showing a plausible lie.
        AddFlag(v, "DA40_STBY_GYRO_TOPPLE", "ATT_GYRO_TOPPLE", "Gyro Toppled", "No", "Yes, toppled");

        // The remaining standby instruments, which have no controls of their own.
        v["DA40_STBY_AIRSPEED"] = new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Backup Airspeed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        v["DA40_STBY_ALTITUDE"] = new SimVarDefinition
        {
            Name = "INDICATED ALTITUDE",
            DisplayName = "Backup Altitude",
            Type = SimVarType.SimVar,
            Units = "feet",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        v["DA40_STBY_COMPASS"] = new SimVarDefinition
        {
            Name = "MAGNETIC COMPASS",
            DisplayName = "Emergency Compass",
            Type = SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        return v;
    }

    private static void AddTrueAttitude(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F1"
        };
    }

    private static readonly List<string> StandbyControls = new()
    {
        "DA40_STBY_ALTIMETER_SET",
        "DA40_STBY_GYRO_CAGE",
        "DA40_STBY_DISPLAY_BACKUP"
    };

    private static readonly List<string> StandbyDisplay = new()
    {
        "DA40_STBY_ALTITUDE",
        "DA40_STBY_AIRSPEED",
        "DA40_STBY_COMPASS",
        "DA40_STBY_GYRO_PITCH",
        "DA40_STBY_GYRO_BANK",
        "DA40_STBY_TRUE_PITCH",
        "DA40_STBY_TRUE_BANK",
        "DA40_STBY_GYRO_CAGED",
        "DA40_STBY_GYRO_SPEED",
        "DA40_STBY_GYRO_TOPPLE"
    };

    /// <summary>
    /// Standby writes. The subscale is a plain latching L:var clamped to the knob's own
    /// travel; the cage knob is held, because the airframe zeroes it every frame.
    /// </summary>
    private bool HandleStandbySet(string varKey, double value, SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_STBY_ALTIMETER_SET":
            {
                // Accept hectopascals or inches. The ranges cannot overlap - inHg runs
                // 28.00 to 31.50 and hPa 948 to 1066 - so magnitude disambiguates.
                double inHg = Math.Clamp(value > 100 ? value / 33.8639 : value, 28.00, 31.50);
                SetStandbyBaro(simConnect, inHg);
                MarkBaroSetByUs();

                // A typed numeric entry gets a spoken confirmation — the pilot needs the
                // exact value back, and it is the one announcement CLAUDE.md explicitly
                // asks for. In BOTH units, since either could have been typed.
                announcer.AnnounceImmediate(
                    $"Standby altimeter {inHg * 33.8639:0} hectopascals, {inHg:0.00} inches");
                return true;
            }

            case "DA40_STBY_GYRO_CAGE":
                // Say what it did. A button that makes a noise and reports nothing is
                // indistinguishable from a button that does nothing.
                HoldLVar("ATT_CAGE", GyroCageHoldMs, simConnect);
                announcer.AnnounceImmediate("Caging standby horizon");
                return true;

            case "DA40_STBY_DISPLAY_BACKUP":
                simConnect.SetLVar("G1000_REV_FORCE", value >= 0.5 ? 1 : 0);
                return true;
        }

        return false;
    }

    /// <summary>
    /// Writes the standby altimeter's subscale.
    ///
    /// ⚠️ NOT through SetLVar. This L:var's name carries a SPACE AND A COLON, and SetLVar
    /// deliberately refuses the calculator path for such names because that shape is
    /// normally a stock SimVar ("TRANSPONDER STATE:1"). It falls back to a native
    /// data-definition write against "L:KOHLSMAN SETTING HG:2" — which lands on the STOCK
    /// SimVar of that name instead, a different variable entirely.
    ///
    /// Measured: writing the L:var through the calculator moved it to 30.11 while the
    /// stock KOHLSMAN SETTING HG:2 stayed at 29.85. So this aeroplane really does have a
    /// standby subscale that only the L:var reaches, exactly as the vendor's own bindings
    /// document says — and the general rule about space/colon names has a real exception
    /// here, which is why this write is spelt out rather than routed.
    ///
    /// Unique, because a pilot setting the same value twice must not have the second write
    /// coalesced away by MobiFlight.
    /// </summary>
    private static void SetStandbyBaro(SimConnectManager simConnect, double inHg)
    {
        simConnect.ExecuteCalculatorCodeUnique(
            inHg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            + " (>L:KOHLSMAN SETTING HG:2)");
    }
}
