using System.Collections.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// ⚠️ NINE READOUTS THE AEROPLANE HAS AND MSFSBA WAS NOT READING, found by diffing FS
/// Copilot's own COWS_DA40NG.yaml against every variable this definition binds.
///
/// FS Copilot syncs a shared cockpit, so its definition is a THIRD-PARTY INVENTORY of what
/// this airframe exposes, written by someone who had to enumerate the state that matters or
/// the two aircraft would drift apart. That makes it a coverage test of a kind the
/// interaction-surface audit structurally cannot be: that audit enumerates CLICKABLE
/// components, and none of these nine is a control - they are all things the aeroplane
/// computes and shows.
///
/// ⚠️ Everything here was READ LIVE before it was defined, because a variable named in a
/// third-party file is a claim, not evidence - the same standard the stock-event names were
/// held to. Values measured on the ground, engine off, avionics master off:
///     COWS_MINIMUMS_ALTITUDE     0          (not set)
///     FUEL_TOTALISER_REM        37.31 gal
///     ELEC_BATT_ECU_CAPACITY   145.95
///     PITOT_TEMP                29.94
///     FUEL_TEMP_C:1             29.74
///     AFCS_POWER                 0          (avionics master off - see below)
///
/// ⚠️ AFCS_POWER READING 0 IS NOT A FAULT AND IS ITSELF A CONFIRMATION. The avionics master
/// was deliberately off, and this aeroplane's systems.cfg puts the AFCS on bus.2 (BUS_AVN).
/// It agreeing with the bus wiring is why it is trustworthy as an autopilot-availability
/// readout: it answers "is the autopilot powered at all", which the GFC 700 panel could not
/// previously say.
///
/// ⚠️ UNITS ARE DECLARED "number" AND SPOKEN THROUGH THE OVERRIDE. Every one of these is an
/// L:var, and an L:var registered with a converting unit makes SimConnect convert from its
/// own base unit and return garbage - this aeroplane's own rule. The word a pilot hears
/// comes from TryGetFsCopilotDisplayOverride.
///
/// ⚠️ ELEC_BATT_ECU_CAPACITY carries NO unit in the readout, deliberately. It measured
/// 145.95, so it is not a percentage, and nothing in the package names its unit - inventing
/// "amp hours" would be a guess presented as fact. A bare number a pilot can watch fall is
/// honest; a wrong unit is not.
/// </summary>
public partial class CowsDA40Definition
{
    private static void AddFsCopilotReadout(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display, string help)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F1",
            HelpText = help
        };
    }

    private static Dictionary<string, SimVarDefinition> BuildFsCopilotFindVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // The G1000's minimums. A blind pilot flying an approach has no other way to read
        // back the DA or MDA they set on the PFD, and 0 genuinely means "not set" - which
        // is a different answer from "I cannot tell you".
        AddFsCopilotReadout(v, "DA40_G1000_MINIMUMS", "COWS_MINIMUMS_ALTITUDE",
            "Minimums Altitude", "What the PFD minimums bug is set to. 0 means not set.");

        // The MFD fuel calculator. ⚠️ NOT the tanks - it is a TOTALISER the pilot sets and
        // which counts down by fuel USED, so it can disagree with the tank gauges by design
        // (measured: tanks 37.2 gal against a calculator showing 32.2 remaining). Both
        // numbers are true and they answer different questions.
        AddFsCopilotReadout(v, "DA40_FUEL_TOTALISER_REM", "FUEL_TOTALISER_REM",
            "Fuel Calculator Remaining", "The MFD totaliser, not the tanks. Gallons.");
        AddFsCopilotReadout(v, "DA40_FUEL_TOTALISER_USED", "FUEL_TOTALISER_USE",
            "Fuel Calculator Used", "Fuel counted as burned since the totaliser was set.");

        // Diesel fuel waxes when it gets cold, so on THIS engine fuel temperature is an
        // operating limit rather than trivia.
        AddFsCopilotReadout(v, "DA40_FUEL_TEMP_LEFT", "FUEL_TEMP_C:1",
            "Left Fuel Temperature", "Celsius. Diesel waxes when cold.");
        AddFsCopilotReadout(v, "DA40_FUEL_TEMP_RIGHT", "FUEL_TEMP_C:2",
            "Right Fuel Temperature", "Celsius. Diesel waxes when cold.");

        // ⚠️ THE ANSWER TO "IS THE PITOT HEAT ACTUALLY WORKING". The switch position and the
        // CAS message both say what was COMMANDED; this is the only thing that says the
        // element is warming. It sits near ambient when off.
        AddFsCopilotReadout(v, "DA40_PITOT_TEMP", "PITOT_TEMP",
            "Pitot Temperature", "Rises when the heat is working. Near ambient when off.");

        // The ECU's own backup battery - what keeps the FADEC alive if the main bus dies.
        // Unit deliberately unnamed; see the class comment.
        AddFsCopilotReadout(v, "DA40_ELEC_BATT_ECU_CAPACITY", "ELEC_BATT_ECU_CAPACITY",
            "ECU Battery Capacity", "The FADEC's backup battery. Falls as it discharges.");
        AddFsCopilotReadout(v, "DA40_ELEC_BATT_SURF", "ELEC_BATT_SURF",
            "Battery Surface Charge", "Surface charge, which recovers after a load is removed.");

        // Is the autopilot POWERED - a different question from whether it is engaged, and
        // one the GFC 700 panel could not answer at all. It lives on the avionics bus.
        AddFsCopilotReadout(v, "DA40_AP_POWERED", "AFCS_POWER",
            "Autopilot Powered", "The AFCS is on the avionics bus, not the battery bus.");

        return v;
    }

    /// <summary>
    /// The unit a pilot HEARS. See the class comment for why it cannot come from Units.
    /// </summary>
    private bool TryGetFsCopilotDisplayOverride(string varKey, double value, out string text)
    {
        switch (varKey)
        {
            case "DA40_G1000_MINIMUMS":
                text = value <= 0 ? "Not set" : $"{value:0} feet";
                return true;

            case "DA40_FUEL_TOTALISER_REM":
            case "DA40_FUEL_TOTALISER_USED":
                text = $"{value:0.0} gallons";
                return true;

            case "DA40_FUEL_TEMP_LEFT":
            case "DA40_FUEL_TEMP_RIGHT":
            case "DA40_PITOT_TEMP":
                text = $"{value:0} degrees C";
                return true;

            case "DA40_AP_POWERED":
                // A bool dressed as a number by the model. Named as a state, not a figure.
                text = value > 0.5 ? "Powered" : "Not powered";
                return true;
        }

        text = "";
        return false;
    }

    /// <summary>Where each find is read. Appended to the panels they belong to.</summary>
    private static readonly List<string> FsCopilotFuelRows = new()
    {
        "DA40_FUEL_TOTALISER_REM", "DA40_FUEL_TOTALISER_USED",
        "DA40_FUEL_TEMP_LEFT", "DA40_FUEL_TEMP_RIGHT"
    };

    private static readonly List<string> FsCopilotElectricalRows = new()
    {
        "DA40_ELEC_BATT_ECU_CAPACITY", "DA40_ELEC_BATT_SURF"
    };

    private static readonly List<string> FsCopilotIcePitotRows = new() { "DA40_PITOT_TEMP" };

    private static readonly List<string> FsCopilotAutopilotRows = new()
    {
        "DA40_AP_POWERED", "DA40_G1000_MINIMUMS"
    };
}
