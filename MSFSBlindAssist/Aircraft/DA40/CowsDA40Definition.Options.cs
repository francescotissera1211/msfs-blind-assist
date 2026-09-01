using System.Collections.Generic;
using System.Globalization;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The COWS aircraft options.
///
/// These live on the MFD's ENGINE page under its Page Menu, which is why they were missed
/// for so long: an audit that walks the pages the FMS knob reaches and reads what renders
/// never opens a page MENU, so every one of these was invisible while that audit reported
/// the MFD complete. The authoritative list is the plugin's own reference set - every
/// <c>L:</c> name in <c>Da40NgMfdPlugin.js</c> - never what happens to be on screen.
///
/// STATE_SAVING_ENABLED is the most consequential switch on the aeroplane and the reason
/// it earns a panel rather than a footnote. The model saves the whole cockpit into
/// <c>STATE_*</c> variables and restores it on load, so a state saved with flat batteries
/// is restored flat every time - measured at VCBI, all three capacities at 0 against
/// factory 230/140/20, which leaves the ECU unpowered and nothing able to crank. Reloading
/// the aircraft does NOT help, because the state reloads with it. Turning this off makes
/// the next load take the factory defaults instead.
///
/// The fuel calculator that sits beside these on the same menu deliberately gets NO panel
/// and no variables: it is a READOUT, the Engine page already draws it, and the display
/// window already reads that page. A panel would be the duplicate this codebase forbids.
/// </summary>
public partial class CowsDA40Definition
{
    private const string OptionsPanel = "Aircraft Options";

    private static void AddOptionSwitch(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string label, string off, string on, string help)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = label,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            HelpText = help,
            ValueDescriptions = new Dictionary<double, string> { [0] = off, [1] = on }
        };
    }

    /// <summary>A COWS option that carries a number rather than a state.</summary>
    private static void AddOptionNumber(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string label, string help)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = label,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            Format = "F0",
            HelpText = help
        };
    }

    private static Dictionary<string, SimVarDefinition> BuildOptionVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddOptionSwitch(v, "DA40_OPT_STATE_SAVING", "STATE_SAVING_ENABLED",
            "State Saving", "Off - load factory fresh", "On - restore last state",
            "Off makes the NEXT load take factory defaults, including full batteries.");

        AddOptionSwitch(v, "DA40_OPT_DAMAGE", "DAMAGE_ENABLED",
            "Engine Damage Modelling", "Off", "On",
            "When on, mishandling the engine does lasting damage that survives a reload.");

        AddOptionSwitch(v, "DA40_OPT_REALISTIC_PARK_BRAKE", "REALISTIC_PARKING_BRAKE",
            "Realistic Parking Brake", "Simplified", "Realistic",
            "Realistic models the real handle, which bleeds off rather than holding for ever.");

        AddOptionSwitch(v, "DA40_OPT_WHEEL_ASSIST", "INPUT_WHEEL_ASSIST",
            "Nosewheel Steering Assist", "Off", "On",
            "The DA40 nosewheel is free-castoring; assist steers it for you.");

        AddOptionSwitch(v, "DA40_OPT_SLOW_PROPS", "SLOW_PROPS",
            "Slow Propeller Animation", "Off", "On",
            "Cosmetic only - slows the prop disc so it does not strobe.");

        AddOptionSwitch(v, "DA40_OPT_PANEL_SHAKE", "PANEL_SHAKE_OFF",
            "Panel Shake Suppressed", "No - panel shakes", "Yes - panel steady",
            "Note the sense: 1 means shake is SUPPRESSED, which is the variable's own name.");

        AddOptionSwitch(v, "DA40_OPT_KILL_FMA", "COWS_KILL_FMA",
            "Hide G1000 FMA", "FMA shown", "FMA hidden",
            "Hides the flight-mode annunciator strip along the top of the PFD.");

        // 0 is off; the model tests 1, 2, 3 and 4 and nothing else. It names none of them,
        // so neither do we - inventing four labels would be a guess presented as fact.
        AddOptionNumber(v, "DA40_OPT_FAILURES_MODE", "FAILURES_MODE",
            "Random Failures Mode",
            "0 is off. The aircraft uses modes 1 to 4; it does not name them.");

        AddOptionNumber(v, "DA40_OPT_TRIM_SPEED", "INPUT_TRIM_SPEED",
            "Electric Trim Speed",
            "Scales how fast the electric trim runs. The model forces 1 if it is ever 0.");

        return v;
    }

    private Dictionary<string, List<string>> OptionPanels()
    {
        return new Dictionary<string, List<string>>
        {
            [OptionsPanel] = new List<string>(OptionControls)
        };
    }

    private static readonly List<string> OptionControls = new()
    {
        "DA40_OPT_STATE_SAVING",
        "DA40_OPT_DAMAGE",
        "DA40_OPT_FAILURES_MODE",
        "DA40_OPT_REALISTIC_PARK_BRAKE",
        "DA40_OPT_WHEEL_ASSIST",
        "DA40_OPT_TRIM_SPEED",
        "DA40_OPT_SLOW_PROPS",
        "DA40_OPT_PANEL_SHAKE",
        "DA40_OPT_KILL_FMA"
    };

    private bool HandleOptionSet(string varKey, double value, SimConnectManager simConnect)
    {
        if (!varKey.StartsWith("DA40_OPT_") || !GetVariables().TryGetValue(varKey, out var def))
        {
            return false;
        }

        // Every one of these is a plain L:var that the MFD writes the same way. Unique
        // because toggling a switch off and straight back on is two byte-identical
        // calculator strings, and MobiFlight would drop the second.
        simConnect.ExecuteCalculatorCodeUnique(string.Format(CultureInfo.InvariantCulture,
            "{0:0.###} (>L:{1})", value, def.Name));
        return true;
    }
}
