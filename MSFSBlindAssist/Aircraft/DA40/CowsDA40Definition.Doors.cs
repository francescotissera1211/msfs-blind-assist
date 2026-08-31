using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Cabin → Doors and Windows. Both variants.
///
/// Four openings: the front canopy, the rear door, and the two storm windows. Each is a
/// stock exit toggled with K:TOGGLE_AIRCRAFT_EXIT, and its position reads back from
/// INTERACTIVE POINT OPEN — but the two indices are OFF BY ONE, which is the trap here.
/// The event index is one higher than the SimVar index, verified live in both directions:
/// event 3 opened INTERACTIVE POINT OPEN:2 to 100 %, event 7 opened :6.
///
///     canopy  event 3 -> :2      storm window left   event 7 -> :6
///     rear    event 4 -> :3      storm window right  event 8 -> :7
///
/// THE AEROPLANE REFUSES ABOVE 30 KNOTS, and does it twice over. The click code is gated
/// on `(A:RELATIVE WIND VELOCITY BODY Z, Knots) abs 30 <=`, so a door will not open into
/// a slipstream; and a 1 Hz Update SLAMS a fully-open canopy or rear door shut the moment
/// relative wind reaches 30. A blind pilot pressing the control and hearing nothing change
/// would have no idea which of those had happened, so the relative wind is on the scan and
/// the refusal is spoken rather than silent.
///
/// The doors report a PERCENTAGE, not a flag - they travel - so mid-travel reads as
/// "Opening" rather than rounding to one end or the other.
/// </summary>
public partial class CowsDA40Definition
{
    private const string DoorsPanel = "Doors and Windows";

    /// <summary>
    /// The model's own threshold, in both the click gate and the auto-close Update.
    /// </summary>
    private const double DoorWindLimitKts = 30.0;

    // Control key -> the K:TOGGLE_AIRCRAFT_EXIT index. The SimVar index is one LOWER, and
    // that difference is the whole reason this table exists rather than a single number.
    private static readonly Dictionary<string, int> DoorExitIndex = new()
    {
        ["DA40_DOOR_CANOPY"] = 3,
        ["DA40_DOOR_REAR"] = 4,
        ["DA40_DOOR_STORM_L"] = 7,
        ["DA40_DOOR_STORM_R"] = 8
    };

    private static Dictionary<string, SimVarDefinition> BuildDoorVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddDoor(v, "DA40_DOOR_CANOPY", 2, "Front Canopy");
        AddDoor(v, "DA40_DOOR_REAR", 3, "Rear Door");
        AddDoor(v, "DA40_DOOR_STORM_L", 6, "Storm Window Left");
        AddDoor(v, "DA40_DOOR_STORM_R", 7, "Storm Window Right");

        // ---------- Status ----------

        // Why a door will not open, and why an open one just shut itself.
        v["DA40_DOOR_WIND"] = new SimVarDefinition
        {
            Name = "RELATIVE WIND VELOCITY BODY Z",
            DisplayName = "Relative Wind",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        return v;
    }

    /// <summary>
    /// A door is a PERCENTAGE that travels, so it is bound to its own position rather than
    /// to a synthetic flag: the value is the truth and mid-travel has somewhere to live.
    /// </summary>
    private static void AddDoor(Dictionary<string, SimVarDefinition> v, string key,
        int simvarIndex, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = $"INTERACTIVE POINT OPEN:{simvarIndex}",
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Closed",
                [100] = "Open"
            }
        };
    }

    private static readonly List<string> DoorControls = new()
    {
        "DA40_DOOR_CANOPY",
        "DA40_DOOR_REAR",
        "DA40_DOOR_STORM_L",
        "DA40_DOOR_STORM_R"
    };

    private static readonly List<string> DoorDisplay = new()
    {
        "DA40_DOOR_WIND"
    };

    private bool HandleDoorSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (!DoorExitIndex.TryGetValue(varKey, out int exit)) return false;

        double wind = Math.Abs(simConnect.GetCachedVariableValue("DA40_AIRSPEED") ?? 0);
        double current = simConnect.GetCachedVariableValue(varKey) ?? 0;
        bool wantOpen = value >= 50;

        // The aeroplane refuses above 30 knots. Say so rather than letting the control
        // appear to do nothing - a refusal and a broken button sound identical otherwise.
        if (wantOpen && current < 50 && wind > DoorWindLimitKts)
        {
            announcer.AnnounceImmediate(
                $"Will not open at {wind:0} knots. The limit is {DoorWindLimitKts:0}.");
            return true;
        }

        // One event, and it TOGGLES - so it is only sent when the door is not already
        // where the pilot asked for it, or picking "Open" on an open door would shut it.
        if (wantOpen != current >= 50)
        {
            simConnect.ExecuteCalculatorCode($"{exit} (>K:TOGGLE_AIRCRAFT_EXIT)");
        }

        return true;
    }

    /// <summary>Mid-travel needs a word: a door at 45 % is neither open nor closed.</summary>
    private bool TryGetDoorDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        if (!DoorExitIndex.ContainsKey(varKey)) return false;

        displayText = value <= 0.5 ? "Closed"
            : value >= 99.5 ? "Open"
            : $"Moving, {value:0} percent";
        return true;
    }
}
