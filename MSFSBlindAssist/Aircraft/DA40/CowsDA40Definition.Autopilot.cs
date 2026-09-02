using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Autopilot → GFC 700 and Flight Director. Both variants.
///
/// The DA40's autopilot is a Garmin GFC 700, and it has NO AIRFRAME VARIABLES OF ITS OWN:
/// the mode controller is part of the G1000, so every button on it is a stock MSFS
/// autopilot event and every light is a stock autopilot SimVar. All fifteen were read live
/// on the aircraft before this panel was written — nothing here is inferred from the name.
///
/// WHY IT IS A PANEL AND NOT THE DISPLAY WINDOW. The standing rule is that anything doable
/// inside a G1000 display does not get a panel. The GFC 700 is the exception the rule is
/// shaped around: its buttons are NOT on the PFD or MFD bezel at all — they are a separate
/// mode controller between the two screens — so there is no display page to drive them
/// from. What the autopilot is DOING still belongs to the display (the FMA, read by the
/// PFD window as "Autopilot: ..."), and is not repeated here.
///
/// ON/OFF EVENTS, NEVER TOGGLES. Every mode takes a distinct _ON and _OFF event rather
/// than a toggle, so a combo set is deterministic: a toggle fired against a state MSFSBA
/// misread would put the mode the other way round, and on an autopilot that is the kind of
/// error a blind pilot cannot see and would only discover from the aeroplane's behaviour.
///
/// THE HEADING BUG AND COURSE MOVED HERE from the Radios panel. They are physically PFD
/// bezel knobs, which is why they started out beside the radio knobs, but functionally
/// they are what the autopilot flies — a pilot selecting HDG mode needs the bug in the
/// same place, not two panels away. They are not duplicated: the Radios panel no longer
/// carries them.
///
/// The AUTOPILOT DISCONNECT is deliberately NOT here. It lives on the Elevator Trim panel
/// because it is the red button on the control stick, next to the trim switch it also
/// interrupts, and one control belongs in one place.
/// </summary>
public partial class CowsDA40Definition
{
    private const string AutopilotPanel = "GFC 700";
    private const string FlightDirectorPanel = "Flight Director";

    private static Dictionary<string, SimVarDefinition> BuildAutopilotVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- TO/GA ----------
        //
        // The go-around button on the power lever. It had no way into MSFSBA at all, which
        // means a blind pilot could not fly a go-around the way the aeroplane is flown -
        // and the go-around is the one manoeuvre where you have no time to work out an
        // attitude for yourself.
        //
        // It is a BUTTON, not a state: pressing it again does not un-press it. There is no
        // variable to read back either, so the button carries no resting state and the
        // aeroplane answers through the flight director's own pitch command, which the
        // Flight Director panel already reads.
        v["DA40_AP_TOGA"] = new SimVarDefinition
        {
            Name = "DA40_AP_TOGA",
            DisplayName = "Go Around (TO/GA)",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false,
            HelpText = "Flight director to wings level, 10 degrees nose up. Verified live."
        };

        AddApMode(v, "DA40_AP_MASTER", "AUTOPILOT MASTER", "Autopilot",
            "Engages the GFC 700. The stick disconnect button also releases it.");
        AddApMode(v, "DA40_AP_YAW_DAMPER", "AUTOPILOT YAW DAMPER", "Yaw Damper",
            "Engages with the autopilot; can be run on its own.");

        // ---------- Lateral modes ----------
        AddApMode(v, "DA40_AP_HDG", "AUTOPILOT HEADING LOCK", "Heading Mode",
            "Flies the heading bug below.");
        AddApMode(v, "DA40_AP_NAV", "AUTOPILOT NAV1 LOCK", "Navigation Mode",
            "Follows the active navigation source - GPS, VOR or localiser.");
        AddApMode(v, "DA40_AP_APR", "AUTOPILOT APPROACH HOLD", "Approach Mode",
            "Arms the localiser and glideslope together.");
        AddApMode(v, "DA40_AP_BC", "AUTOPILOT BACKCOURSE HOLD", "Backcourse Mode",
            "For a localiser flown from the back beam.");

        // ---------- Vertical modes ----------
        AddApMode(v, "DA40_AP_ALT", "AUTOPILOT ALTITUDE LOCK", "Altitude Hold",
            "Holds the altitude at the moment of engagement, not the selected one.");
        AddApMode(v, "DA40_AP_VS", "AUTOPILOT VERTICAL HOLD", "Vertical Speed Mode",
            "Flies the vertical speed below.");
        AddApMode(v, "DA40_AP_FLC", "AUTOPILOT FLIGHT LEVEL CHANGE", "Flight Level Change",
            "Climbs or descends at the selected airspeed, pitching for speed.");

        // ---------- Selected values ----------
        AddApValue(v, "DA40_AP_ALT_SET", "AUTOPILOT ALTITUDE LOCK VAR", "Selected Altitude",
            "feet", "F0", "The altitude the autopilot captures. Feet.");
        AddApValue(v, "DA40_AP_VS_SET", "AUTOPILOT VERTICAL HOLD VAR", "Selected Vertical Speed",
            "feet per minute", "F0", "Feet per minute. Negative to descend.");
        AddApValue(v, "DA40_AP_IAS_SET", "AUTOPILOT AIRSPEED HOLD VAR", "Selected Airspeed",
            "knots", "F0", "The speed flight level change pitches for. Knots.");
        AddApValue(v, "DA40_AP_HDG_SET", "AUTOPILOT HEADING LOCK DIR", "Heading Bug",
            "degrees", "F0", "0 to 359. What heading mode flies.");
        AddApValue(v, "DA40_AP_CRS_SET", "NAV OBS:1", "Course",
            "degrees", "F0", "0 to 359. The NAV 1 course pointer.");

        // ---------- Flight Director ----------
        v["DA40_AP_FD"] = new SimVarDefinition
        {
            Name = "AUTOPILOT FLIGHT DIRECTOR ACTIVE:1",
            DisplayName = "Flight Director",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
            HelpText = "Command bars. The autopilot turns it on by itself when engaged."
        };

        // The command attitude the bars are showing. A sighted pilot flies the bars by
        // matching them; a blind pilot cannot see them at all, so the numbers ARE the
        // bars - and they are what makes a hand-flown departure to an FD command possible.
        // ⚠️ BOTH ARE RENDERED THROUGH TryGetDisplayOverride, NEVER as a raw signed number.
        // MSFS reports pitch NEGATIVE for nose up - established here rather than assumed:
        // the aeroplane sitting on its gear reads PLANE PITCH DEGREES -2.90 with
        // ATTITUDE INDICATOR PITCH DEGREES agreeing at -2.90, and a DA40 on its gear sits
        // nose UP. So a commanded -3.0 is three degrees nose UP, and the old help text
        // saying "Positive is nose up" had it exactly backwards. That is not cosmetic: a
        // blind pilot hand-flying to this number would push when they should pull.
        AddApValue(v, "DA40_AP_FD_PITCH", "AUTOPILOT FLIGHT DIRECTOR PITCH", "Commanded Pitch",
            "degrees", "F1", "Spoken as nose up or nose down; MSFS reports it inverted.");
        AddApValue(v, "DA40_AP_FD_BANK", "AUTOPILOT FLIGHT DIRECTOR BANK", "Commanded Bank",
            "degrees", "F1", "Spoken as left or right; MSFS reports bank left-positive.");

        return v;
    }

    /// <summary>
    /// The flight director bars, as words rather than a signed number.
    ///
    /// SIGN. MSFS reports pitch NEGATIVE for nose up and bank LEFT-positive - the same
    /// left-positive convention the visual-guidance tone already has to undo. Read raw,
    /// a three-degree nose-up command was spoken as "-3", which a pilot flying the bars
    /// would answer by pushing.
    ///
    /// DENORMALS. Bank was measured live at 1.43e-305, which is not a small angle, it is
    /// floating-point dust. Anything under a twentieth of a degree is level.
    /// </summary>
    private static string DescribeFlightDirector(string varKey, double value)
    {
        // Undo the sign here, once, so everything downstream reads naturally.
        double v = -value;
        if (Math.Abs(v) < 0.05) v = 0;

        string magnitude = Math.Abs(v).ToString("F1",
            System.Globalization.CultureInfo.InvariantCulture);

        if (varKey == "DA40_AP_FD_PITCH")
        {
            if (v == 0) return "level";
            return magnitude + " degrees nose " + (v > 0 ? "up" : "down");
        }

        if (v == 0) return "wings level";
        return magnitude + " degrees " + (v > 0 ? "right" : "left");
    }

    /// <summary>An autopilot mode: a two-state combo backed by a stock SimVar.</summary>
    private static void AddApMode(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display, string help)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
            HelpText = help
        };
    }

    /// <summary>
    /// A selected value: a typed entry, never a slider. MainForm's TrackBar is hardcoded
    /// 0-100 and maps its value as a percentage of that range, which is meaningless for an
    /// altitude or a vertical speed; a key ending _SET renders as a typed field instead.
    /// </summary>
    private static void AddApValue(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display, string units, string format, string help)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = units,
            UpdateFrequency = UpdateFrequency.Continuous,
            // ⚠️ REQUIRED, and it is not about announcing. Batch membership is
            // Continuous AND IsAnnounced, so with this false the selected values never
            // reached the cache at all: output mode + A answered "Selected not available",
            // and the settle announcer never saw a knob move because ProcessSimVarUpdate
            // was never called for them. Exactly the fault the radios had. The generic
            // announcer is kept out by NoteRadioChange returning true, not by this flag.
            IsAnnounced = true,
            Format = format,
            HelpText = help
        };
    }

    private static readonly List<string> AutopilotControls = new()
    {
        "DA40_AP_TOGA",
        "DA40_AP_MASTER",
        "DA40_AP_YAW_DAMPER",
        "DA40_AP_HDG",
        "DA40_AP_NAV",
        "DA40_AP_APR",
        "DA40_AP_BC",
        "DA40_AP_ALT",
        "DA40_AP_VS",
        "DA40_AP_FLC",
        "DA40_AP_ALT_SET",
        "DA40_AP_VS_SET",
        "DA40_AP_IAS_SET",
        "DA40_AP_HDG_SET",
        "DA40_AP_CRS_SET"
    };

    /// <summary>
    /// The Ctrl+3 status display.
    ///
    /// This was EMPTIED once, on the reasoning that every selected value is already a
    /// control and a control shows its own value. The reasoning was wrong and the cost was
    /// immediate: Ctrl+3 on the GFC 700 panel produced a status display with nothing in it
    /// at all. A panel's controls and its STATUS DISPLAY answer different questions - one
    /// is what you operate, the other is what you sweep to see where the autopilot stands -
    /// and a pilot checking the selected altitude before a climb wants the second.
    ///
    /// It was then restored, and the suite objected AGAIN and was right the second time:
    /// ScansDoNotRepeatControls exists because a control reads its own position when you
    /// tab to it, so repeating it on the scan is duplication that also drags an announcing
    /// variable into a list meant to be silent.
    ///
    /// So it stays empty, and that is the honest answer for THIS panel: every meaningful
    /// item on the GFC 700 is something you operate, and there is no read-only state left
    /// over to sweep. The pilot's real need - knowing what is selected without tabbing
    /// through five controls - is met twice over instead: the values ANNOUNCE THEMSELVES
    /// on settle when anything moves them, and output mode + A, H, S and V answer with the
    /// current value AND the selected one together.
    /// </summary>
    // Kept as a named empty list so the reasoning above has something to hang on, but
    // the panel no longer registers a display entry at all - see CowsDA40Definition.cs.
    private static readonly List<string> AutopilotDisplay = new();

    private static readonly List<string> FlightDirectorControls = new() { "DA40_AP_FD" };

    private static readonly List<string> FlightDirectorDisplay = new()
    {
        "DA40_AP_FD_PITCH",
        "DA40_AP_FD_BANK"
    };

    /// <summary>
    /// Every mode takes its own ON and OFF event, so a combo set lands where the pilot
    /// asked regardless of what MSFSBA believed the state was.
    /// </summary>
    private bool HandleAutopilotSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        bool on = value >= 0.5;

        string? onOff = varKey switch
        {
            "DA40_AP_MASTER" => on ? "AUTOPILOT_ON" : "AUTOPILOT_OFF",
            "DA40_AP_YAW_DAMPER" => on ? "YAW_DAMPER_ON" : "YAW_DAMPER_OFF",
            "DA40_AP_HDG" => on ? "AP_HDG_HOLD_ON" : "AP_HDG_HOLD_OFF",
            "DA40_AP_NAV" => on ? "AP_NAV1_HOLD_ON" : "AP_NAV1_HOLD_OFF",
            "DA40_AP_APR" => on ? "AP_APR_HOLD_ON" : "AP_APR_HOLD_OFF",
            "DA40_AP_BC" => on ? "AP_BC_HOLD_ON" : "AP_BC_HOLD_OFF",
            "DA40_AP_ALT" => on ? "AP_ALT_HOLD_ON" : "AP_ALT_HOLD_OFF",
            "DA40_AP_VS" => on ? "AP_VS_HOLD_ON" : "AP_VS_HOLD_OFF",
            "DA40_AP_FLC" => on ? "FLIGHT_LEVEL_CHANGE_ON" : "FLIGHT_LEVEL_CHANGE_OFF",
            _ => null
        };

        // ---------- TO/GA ----------
        //
        // The go-around button on the power lever, and the one control on this autopilot
        // that had no way in at all. The vendor's own bindings name it
        // KEY_AUTO_THROTTLE_TO_GA, which is a misleading name on an aeroplane with no
        // autothrottle: what it actually does here is command the FLIGHT DIRECTOR.
        //
        // Verified live rather than assumed: with the flight director off and its pitch
        // command at zero, one AUTO_THROTTLE_TO_GA turned the director ON and set the pitch
        // command to 10 degrees NOSE UP - wings level, ten degrees, which is the go-around
        // attitude. (The SimVar reports -10; MSFS signs flight-director pitch the other way
        // round, which is why DescribeFlightDirector negates it.)
        //
        // It is a BUTTON, not a switch: pressing it again does not un-press it, and the way
        // out of the mode is to select another one or to disconnect.
        if (varKey == "DA40_AP_TOGA")
        {
            simConnect.ExecuteCalculatorCodeUnique("1 (>K:AUTO_THROTTLE_TO_GA)");
            announcer.AnnounceImmediate("Go around, flight director 10 degrees nose up");
            return true;
        }

        if (onOff != null)
        {
            // Unique, because a mode selected twice running is the same string twice and
            // MobiFlight coalesces byte-identical consecutive commands.
            simConnect.ExecuteCalculatorCodeUnique($"1 (>K:{onOff})");
            return true;
        }

        switch (varKey)
        {
            // The flight director is a toggle with no ON/OFF pair, so it is compared in
            // RPN rather than read in C# and written back - the same shape the electrical
            // switches use, and for the same reason.
            case "DA40_AP_FD":
                simConnect.ExecuteCalculatorCode(
                    $"(A:AUTOPILOT FLIGHT DIRECTOR ACTIVE:1, Bool) {(on ? 0 : 1)} == " +
                    "if{ 1 (>K:TOGGLE_FLIGHT_DIRECTOR) }");
                return true;

            case "DA40_AP_ALT_SET":
            {
                MarkRadioSetByUs();
                // The selected altitude is entered in FEET and the event takes feet, but
                // the aeroplane's ceiling is the sane clamp: an entry slip of one digit
                // would otherwise command a climb the aircraft cannot make.
                int feet = (int)Math.Clamp(Math.Round(value / 100.0) * 100.0, 0, 18000);
                simConnect.ExecuteCalculatorCodeUnique($"{feet} (>K:AP_ALT_VAR_SET_ENGLISH)");
                announcer.AnnounceImmediate($"Selected altitude {feet} feet");
                return true;
            }

            case "DA40_AP_VS_SET":
            {
                MarkRadioSetByUs();
                int fpm = (int)Math.Clamp(Math.Round(value / 100.0) * 100.0, -2000, 2000);
                simConnect.ExecuteCalculatorCodeUnique($"{fpm} (>K:AP_VS_VAR_SET_ENGLISH)");
                announcer.AnnounceImmediate(fpm == 0
                    ? "Selected vertical speed level"
                    : $"Selected vertical speed {Math.Abs(fpm)} feet per minute {(fpm > 0 ? "up" : "down")}");
                return true;
            }

            case "DA40_AP_IAS_SET":
            {
                MarkRadioSetByUs();
                // Between the flap-limit end of the envelope and Vne. Flight level change
                // pitches for this speed, so a value outside the envelope is a command to
                // stall or to overspeed.
                int kt = (int)Math.Clamp(Math.Round(value), 60, 172);
                simConnect.ExecuteCalculatorCodeUnique($"{kt} (>K:AP_SPD_VAR_SET)");
                announcer.AnnounceImmediate($"Selected airspeed {kt} knots");
                return true;
            }

            case "DA40_AP_HDG_SET":
            {
                MarkRadioSetByUs();
                int deg = ((int)Math.Round(value) % 360 + 360) % 360;
                simConnect.ExecuteCalculatorCodeUnique($"{deg} (>K:HEADING_BUG_SET)");
                announcer.AnnounceImmediate($"Heading bug {deg:000}");
                return true;
            }

            case "DA40_AP_CRS_SET":
            {
                MarkRadioSetByUs();
                int deg = ((int)Math.Round(value) % 360 + 360) % 360;
                simConnect.ExecuteCalculatorCodeUnique($"{deg} (>K:VOR1_SET)");
                announcer.AnnounceImmediate($"Course {deg:000}");
                return true;
            }
        }

        return false;
    }
}
