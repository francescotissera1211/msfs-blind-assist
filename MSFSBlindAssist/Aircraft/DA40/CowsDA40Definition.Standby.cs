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

    /// <summary>POH/AFM: the cage knob is pulled and held briefly, not clicked.</summary>
    private const int GyroCageHoldMs = 2500;

    private static Dictionary<string, SimVarDefinition> BuildStandbyVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_STBY_ALTIMETER_SET"] = new SimVarDefinition
        {
            Name = "KOHLSMAN SETTING HG:2",
            DisplayName = "Standby Altimeter Setting",
            Type = SimVarType.LVar,
            Units = "inHg",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsSlider = true,
            SliderMin = 28.00,
            SliderMax = 31.50,
            Format = "F2",
            HelpText = "The backup altimeter has its own subscale, separate from the G1000's. " +
                       "The AFM descent checklist item reads \"Altimeters (2) SET\" — both " +
                       "need setting. Range 28.00 to 31.50 inches of mercury."
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
            HelpText = "Pulls and holds the cage knob to re-erect the backup artificial " +
                       "horizon. Cage it level — caging while pitched or banked sets the " +
                       "error you cage in."
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
            HelpText = "Forces the G1000 into reversionary mode, putting the primary flight " +
                       "display onto the remaining screen after a display failure."
        };

        // ---------- Status ----------

        AddReadout(v, "DA40_STBY_ALT_SETTING_STATE", "STATE_BARO2", "Standby Subscale", "inHg", "F2");

        AddFlag(v, "DA40_STBY_GYRO_CAGED", "ATT_GYRO_CAGE_SET", "Gyro Caged", "No", "Yes");

        // The standby horizon's own attitude, which is the whole point of having it.
        AddReadout(v, "DA40_STBY_GYRO_PITCH", "ATT_GYRO_REL_PITCH", "Standby Horizon Pitch", "degrees", "F1");
        AddReadout(v, "DA40_STBY_GYRO_BANK", "ATT_GYRO_REL_BANK", "Standby Horizon Bank", "degrees", "F1");

        // Spin-up and topple. A toppled gyro reads a plausible lie; without these there is
        // no way to know the instrument has stopped being trustworthy.
        AddReadout(v, "DA40_STBY_GYRO_SPEED", "ATT_GYRO_SPEED", "Gyro Spin", "of 1", "F2");
        AddReadout(v, "DA40_STBY_GYRO_TOPPLE", "ATT_GYRO_TOPPLE", "Gyro Topple", "", "F2");
        AddReadout(v, "DA40_STBY_GYRO_RIGID", "ATT_GYRO_RIGID", "Gyro Rigidity", "", "F2");

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

    private static readonly List<string> StandbyControls = new()
    {
        "DA40_STBY_ALTIMETER_SET",
        "DA40_STBY_GYRO_CAGE",
        "DA40_STBY_DISPLAY_BACKUP"
    };

    private static readonly List<string> StandbyDisplay = new()
    {
        "DA40_STBY_ALT_SETTING_STATE",
        "DA40_STBY_ALTITUDE",
        "DA40_STBY_AIRSPEED",
        "DA40_STBY_COMPASS",
        "DA40_STBY_GYRO_PITCH",
        "DA40_STBY_GYRO_BANK",
        "DA40_STBY_GYRO_CAGED",
        "DA40_STBY_GYRO_SPEED",
        "DA40_STBY_GYRO_RIGID",
        "DA40_STBY_GYRO_TOPPLE"
    };

    /// <summary>
    /// Standby writes. The subscale is a plain latching L:var clamped to the knob's own
    /// travel; the cage knob is held, because the airframe zeroes it every frame.
    /// </summary>
    private bool HandleStandbySet(string varKey, double value, SimConnectManager simConnect)
    {
        switch (varKey)
        {
            case "DA40_STBY_ALTIMETER_SET":
                simConnect.SetLVar("KOHLSMAN SETTING HG:2", Math.Clamp(value, 28.00, 31.50));
                return true;

            case "DA40_STBY_GYRO_CAGE":
                HoldLVar("ATT_CAGE", GyroCageHoldMs, simConnect);
                return true;

            case "DA40_STBY_DISPLAY_BACKUP":
                simConnect.SetLVar("G1000_REV_FORCE", value >= 0.5 ? 1 : 0);
                return true;
        }

        return false;
    }
}
