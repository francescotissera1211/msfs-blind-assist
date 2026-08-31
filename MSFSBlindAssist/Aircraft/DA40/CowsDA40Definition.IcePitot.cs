using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Ice and Pitot.
///
/// Three controls, each named in the AFM. Pitot Heat is legend item 3 and the
/// Alternate static valve is item 15; Alternate Air is not on the legend but appears
/// throughout the procedures as an OPEN/CLOSED item — before take-off "check CLOSED",
/// and OPEN in the icing and engine-trouble drills — so it is a first-class control,
/// not an afterthought.
///
/// All three were written and read back live, each with a downstream effect rather than
/// a self-echo:
///   Pitot Heat      K:PITOT_HEAT_ON/OFF   ->  A:PITOT HEAT 0/1, L:STATE_PITOT follows
///   Alternate Air   L:ENGINE_ALTERNATE_AIR ->  L:ENG_ALT_AIR_FACTOR moved 1.00 to 0.98
///   Alternate Static K:TOGGLE_ALTERNATE_STATIC -> A:ALTERNATE STATIC SOURCE OPEN 0/1
///
/// Pitot heat has a CAS consequence worth knowing about: with it OFF the G1000 shows
/// PITOT HT OFF, and with it ON while the aeroplane is stationary on the ground the
/// airframe raises PITOT FAIL. Both are modelled behaviour, not artefacts — the CAS
/// panel reports them verbatim.
///
/// The ice/wing inspection light is NOT here. It is a light, it lives on the Lighting
/// panel, and no control is duplicated across panels.
/// </summary>
public partial class CowsDA40Definition
{
    private const string IcePitotPanel = "Ice and Pitot";

    private static Dictionary<string, SimVarDefinition> BuildIcePitotVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        v["DA40_ICE_PITOT_HEAT"] = new SimVarDefinition
        {
            Name = "PITOT HEAT",
            DisplayName = "Pitot Heat",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
            HelpText = "With this off the G1000 shows PITOT HT OFF. Switched on while " +
                       "stationary on the ground the aeroplane raises PITOT FAIL — that is " +
                       "modelled behaviour, not a fault in the readout."
        };

        v["DA40_ICE_ALTERNATE_AIR"] = new SimVarDefinition
        {
            Name = "ENGINE_ALTERNATE_AIR",
            DisplayName = "Alternate Air",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" },
            HelpText = "Engine induction air. The AFM has it CLOSED for take-off, and OPEN " +
                       "for unintentional flight into icing and for engine trouble."
        };

        v["DA40_ICE_ALTERNATE_STATIC"] = new SimVarDefinition
        {
            Name = "ALTERNATE STATIC SOURCE OPEN",
            DisplayName = "Alternate Static Valve",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" },
            HelpText = "Opens the cabin static source if the external static port blocks. " +
                       "Expect the altimeter and airspeed to shift when it is opened."
        };

        // ---------- Status ----------

        AddFlag(v, "DA40_ICE_PITOT_STATE", "STATE_PITOT", "Pitot Heat State", "Off", "On");
        AddFlag(v, "DA40_ICE_ALT_AIR_STATE", "STATE_ALTERNATE_AIR", "Alternate Air State", "Closed", "Open");
        // Moves off 1.00 when alternate air opens — the induction restriction, so the
        // pilot can see the door is actually doing something.
        AddReadout(v, "DA40_ICE_ALT_AIR_FACTOR", "ENG_ALT_AIR_FACTOR", "Induction Air Factor", "", "F2");
        AddReadout(v, "DA40_ICE_OAT", "ABS_AMBIENT_TEMPERATURE", "Outside Air Temperature", "celsius", "F0");
        AddReadout(v, "DA40_ICE_STBY_DIFF_PRESSURE", "STBY_DIFFERENTIAL_PRESSURE",
            "Standby Differential Pressure", "", "F0");

        return v;
    }

    private static readonly List<string> IcePitotControls = new()
    {
        "DA40_ICE_PITOT_HEAT",
        "DA40_ICE_ALTERNATE_AIR",
        "DA40_ICE_ALTERNATE_STATIC"
    };

    private static readonly List<string> IcePitotDisplay = new()
    {
        "DA40_ICE_PITOT_STATE",
        "DA40_ICE_ALT_AIR_STATE",
        "DA40_ICE_ALT_AIR_FACTOR",
        "DA40_ICE_ALTERNATE_STATIC",
        "DA40_ICE_OAT",
        "DA40_ICE_STBY_DIFF_PRESSURE"
    };

    /// <summary>
    /// Ice and pitot writes. Pitot heat and the static valve have discrete ON/OFF and
    /// TOGGLE events respectively; alternate air is a plain latching L:var.
    /// </summary>
    private bool HandleIcePitotSet(string varKey, double value, SimConnectManager simConnect)
    {
        bool on = value >= 0.5;

        switch (varKey)
        {
            case "DA40_ICE_PITOT_HEAT":
                simConnect.ExecuteCalculatorCode(on ? "(>K:PITOT_HEAT_ON)" : "(>K:PITOT_HEAT_OFF)");
                return true;

            case "DA40_ICE_ALTERNATE_AIR":
                simConnect.SetLVar("ENGINE_ALTERNATE_AIR", on ? 1 : 0);
                return true;

            case "DA40_ICE_ALTERNATE_STATIC":
                // Only a toggle exists, so compare first and the combo stays idempotent.
                simConnect.ExecuteCalculatorCode(
                    $"(A:ALTERNATE STATIC SOURCE OPEN, Bool) {(on ? 0 : 1)} == " +
                    "if{ (>K:TOGGLE_ALTERNATE_STATIC) }");
                return true;
        }

        return false;
    }
}
