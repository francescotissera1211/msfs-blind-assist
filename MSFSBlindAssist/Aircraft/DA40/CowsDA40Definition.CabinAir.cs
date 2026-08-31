using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Cabin Heat and Vent. Both variants.
///
/// TWO LEVERS, AND THIS PANEL WAS WRONGLY DELETED ONCE. An earlier pass concluded COWS
/// modelled neither cabin heat nor ventilation — "no component in the interaction XML, no
/// L:var, no simvar" — and removed the panel. Both are there:
///
///     ASOBO_PASSENGER_Lever_Cabin_Air_Template   on PRESSURIZATION_Switch_Bleed
///     ASOBO_PASSENGER_Lever_Cabin_Heat_Template  on PASSENGER_Switch_Cabin_Heat
///
/// inside `Component ID="PASSENGER"`. The sweep missed them for two reasons worth
/// remembering: the component is named after the OCCUPANTS rather than the system, and the
/// air lever's node is called PRESSURIZATION_Switch_Bleed — a name Asobo reuses across
/// aircraft — on an aeroplane that has no pressurization at all. Searching for the system
/// found nothing; the templates are what name it.
///
/// They drive L:XMLVAR_CabinAir and L:XMLVAR_CabinHeat, 0 to 100, both verified live to
/// hold a write.
///
/// THE DA40 IS NOT PRESSURIZED. The AFM does not contain the word, and neither lever has
/// anything to do with cabin altitude: heat comes off the engine heat exchanger and air
/// through the nozzles. There is no air conditioning modelled either — the AFM never
/// mentions it, and the cockpit has no control for one.
/// </summary>
public partial class CowsDA40Definition
{
    private const string CabinAirPanel = "Cabin Heat and Vent";

    private static Dictionary<string, SimVarDefinition> BuildCabinAirVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // A genuine 0-100 percentage, so a slider is right here — unlike the trim and the
        // standby subscale, whose ranges MainForm's TrackBar cannot express.
        AddCabinLever(v, "DA40_CABIN_HEAT", "XMLVAR_CabinHeat", "Cabin Heat",
            "Engine heat exchanger. Closed to open, 0 to 100 percent.");
        AddCabinLever(v, "DA40_CABIN_AIR", "XMLVAR_CabinAir", "Cabin Air",
            "Fresh air to the cabin. 0 to 100 percent.");

        return v;
    }

    private static void AddCabinLever(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display, string help)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            RenderAsSlider = true,
            Format = "F0",
            HelpText = help
        };
    }

    private static readonly List<string> CabinAirControls = new()
    {
        "DA40_CABIN_HEAT",
        "DA40_CABIN_AIR"
    };

    private bool HandleCabinAirSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (varKey != "DA40_CABIN_HEAT" && varKey != "DA40_CABIN_AIR") return false;

        double pct = Math.Clamp(value, 0, 100);
        simConnect.SetLVar(varKey == "DA40_CABIN_HEAT" ? "XMLVAR_CabinHeat" : "XMLVAR_CabinAir", pct);
        return true;
    }
}
