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

    // ==================================================================================
    // SECOND PASS: everything else the YAML named that the aeroplane really has.
    //
    // WARNING: THE YAML IS A LEAD SOURCE, NOT AN ORACLE - PROVEN, not assumed. Checking
    // every candidate against the PACKAGE ITSELF before defining it found ELEVEN names FS
    // Copilot lists that the aircraft does not contain at all: AFCS_FAIL_AIL/ELE/TRIM (the
    // real ones are FAILURES_AFCS_*, which this definition already had), the whole
    // FAILURES_SENS_* family, and LIGHTING_PANEL_1 / LIGHTING_GLARESHIELD_1 (the real
    // brightness inputs are LIGHT POTENTIOMETER, already bound). Almost certainly leftovers
    // from an older build of the aeroplane.
    //
    // Binding those would have produced eleven readouts sitting at 0 forever, quietly
    // reporting "no failure" about a system nothing is watching - worse than not having
    // them, because a pilot would scan them and be reassured. The package scan is what
    // separates a claim from evidence, and it costs one pass over 1.2 MB of XML.
    // ==================================================================================

    private static void AddFind(Dictionary<string, SimVarDefinition> v, string key, string name,
        SimVarType type, string display, string help, string units = "number",
        string format = "F1", Dictionary<double, string>? states = null, bool announce = false)
    {
        var d = new SimVarDefinition
        {
            Name = name,
            DisplayName = display,
            Type = type,
            Units = units,
            UpdateFrequency = announce ? UpdateFrequency.Continuous : UpdateFrequency.OnRequest,
            IsAnnounced = announce,
            Format = format,
            HelpText = help
        };
        if (states != null) d.ValueDescriptions = states;
        else { d.RenderAsReadOnlyStatus = true; d.ExcludeFromMonitorManager = true; }
        v[key] = d;
    }

    private static Dictionary<string, SimVarDefinition> BuildFsCopilotSecondPassVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        var yesNo = new Dictionary<double, string> { [0] = "No", [1] = "Yes" };

        // ---------- THE TWO THAT MUST INTERRUPT ----------
        //
        // WARNING: NEITHER WAS BOUND AT ALL. An engine fire and an engine failure are the
        // two states on this aeroplane a pilot must not have to go looking for, and MSFSBA
        // could not see either. The fire is even referenced in the fuel-valve
        // documentation - "turning the valve OFF really does clear ENG ON FIRE:1" - so it
        // was known to exist and still never read.
        AddFind(v, "DA40_ENG_FIRE", "ENG ON FIRE:1", SimVarType.SimVar, "Engine Fire",
            "The AFM procedure is the fuel valve to OFF, which needs the wire broken.",
            "bool", "F0", new Dictionary<double, string> { [0] = "No", [1] = "FIRE" }, true);

        AddFind(v, "DA40_ENG_FAILED", "GENERAL ENG FAILED:1", SimVarType.SimVar,
            "Engine Failed", "The engine has failed outright, not merely degraded.",
            "bool", "F0", yesNo, true);

        // ---------- ENGINE THERMAL, AT FULL RESOLUTION ----------
        //
        // WARNING: WC_TEMP_BLOCK IS THE ONE WITH A LIMIT ATTACHED. The model damages the
        // block directly above 140 C, with no failure involved and regardless of the damage
        // switch - so it is the temperature that can wreck the engine while every gauge a
        // pilot can read stays green. Measured 84.99 C on the ground.
        AddFind(v, "DA40_ENG_BLOCK_TEMP", "WC_TEMP_BLOCK:1", SimVarType.LVar,
            "Block Temperature", "Celsius. The model damages the block above 140.");
        AddFind(v, "DA40_ENG_RAD_TEMP", "WC_TEMP_RAD:1", SimVarType.LVar,
            "Radiator Temperature", "Celsius. Measured 37.6 on the ground.");
        AddFind(v, "DA40_ENG_THERMOSTAT", "WC_THERMOSTAT:1", SimVarType.LVar,
            "Thermostat", "How far the thermostat has opened.");
        AddFind(v, "DA40_ENG_GEARBOX_TEMP", "GC_GEARBOX_TEMPERATURE:1", SimVarType.LVar,
            "Gearbox Temperature", "Celsius, from the sensor rather than the display var.");

        // The coolant, both halves: how much is left and how fast it is going.
        AddFind(v, "DA40_ENG_COOLANT_LEVEL", "RECIP ENG COOLANT RESERVOIR PERCENT:1",
            SimVarType.SimVar, "Coolant Reservoir",
            "Percent remaining. Falls when the coolant-leak failure is active.",
            "percent", "F0");
        AddFind(v, "DA40_ENG_COOLANT_LEAK_RATE", "ENG_COOLANT_LEAK:1", SimVarType.LVar,
            "Coolant Leak", "How much coolant is being lost.");

        // ---------- DAMAGE THE HEALTH FIGURES DO NOT COVER ----------
        //
        // WARNING: These are SEPARATE accumulators, not components of HEALTH_*. Turbo
        // friction and fuel oscillation each track their own wear, so an engine can be
        // accumulating damage that every health percentage still reports as fine.
        AddFind(v, "DA40_DAMAGE_TURBO_FRICTION", "DAMAGE_TURBO_FRIC:1", SimVarType.LVar,
            "Turbo Friction Damage", "Tracked separately from turbocharger health.");
        AddFind(v, "DA40_DAMAGE_FUEL_OSCILLATION", "DAMAGE_FUEL_OSCI:1", SimVarType.LVar,
            "Fuel Oscillation Damage", "Tracked separately from fuel-system health.");

        // ---------- THE ECUs, BEYOND PASS/FAIL ----------
        AddFind(v, "DA40_ECU_A_STARTING", "FADEC_START_ECU_A:1", SimVarType.LVar,
            "ECU A Starting", "Which ECU is running the start.", "number", "F0", yesNo);
        AddFind(v, "DA40_ECU_B_STARTING", "FADEC_START_ECU_B:1", SimVarType.LVar,
            "ECU B Starting", "Which ECU is running the start.", "number", "F0", yesNo);
        AddFind(v, "DA40_ECU_A_FAIL_TIME", "FADEC_ECU_FAIL_TIME_A:1", SimVarType.LVar,
            "ECU A Fault Timer", "Counts while ECU A is faulted.");
        AddFind(v, "DA40_ECU_B_FAIL_TIME", "FADEC_ECU_FAIL_TIME_B:1", SimVarType.LVar,
            "ECU B Fault Timer", "Counts while ECU B is faulted.");

        // ---------- WHAT THE AUTOPILOT SERVOS ARE PULLING AGAINST ----------
        //
        // The one honest answer to "is the autopilot fighting me". A servo working hard is
        // how an out-of-trim aeroplane shows itself before the autopilot gives up.
        AddFind(v, "DA40_AP_FORCE_AILERON", "AFCS_FORCE_AIL", SimVarType.LVar,
            "Aileron Servo Force", "What the roll servo is holding.");
        AddFind(v, "DA40_AP_FORCE_ELEVATOR", "AFCS_FORCE_ELE", SimVarType.LVar,
            "Elevator Servo Force", "What the pitch servo is holding.");

        // ---------- TO/GA, WHICH THE BUTTON SAID COULD NOT BE READ BACK ----------
        //
        // WARNING: CORRECTION TO THIS DEFINITION'S OWN COMMENT. The TO/GA button was
        // documented as carrying "no resting state and no variable to read back", so the
        // aeroplane was said to answer only through the flight director's pitch command.
        // WT_TOGA_ACTIVE is exactly that read-back, and it was in the package all along.
        AddFind(v, "DA40_AP_TOGA_ACTIVE", "WT_TOGA_ACTIVE", SimVarType.LVar,
            "Go Around Mode", "Whether TO/GA is engaged.", "number", "F0",
            new Dictionary<double, string> { [0] = "Off", [1] = "Active" }, true);

        // ---------- THE START, WHILE IT IS HAPPENING ----------
        AddFind(v, "DA40_START_AUTOSTART_STEP", "AUTOSTART_STEP", SimVarType.LVar,
            "Auto Start Step", "Which step the aircraft's own auto-start has reached.",
            "number", "F0");
        AddFind(v, "DA40_START_INPUT", "INPUT_START", SimVarType.LVar,
            "Start Input", "How far the start control is being held.", "percent", "F0");

        // ---------- FAILURES THAT HAD NO ROW ----------
        AddFind(v, "DA40_FAIL_FUEL_LEFT", "FAILURES_FUEL_L", SimVarType.LVar,
            "Left Fuel Failure", "Left fuel system failure.", "number", "F0", yesNo, true);
        AddFind(v, "DA40_FAIL_FUEL_RIGHT", "FAILURES_FUEL_R", SimVarType.LVar,
            "Right Fuel Failure", "Right fuel system failure.", "number", "F0", yesNo, true);
        AddFind(v, "DA40_FAIL_WASTEGATE_A", "FAILURES_WASTEGATE_A:1", SimVarType.LVar,
            "Wastegate A Failure", "Wastegate channel A.", "number", "F0", yesNo, true);
        AddFind(v, "DA40_FAIL_WASTEGATE_B", "FAILURES_WASTEGATE_B:1", SimVarType.LVar,
            "Wastegate B Failure", "Wastegate channel B.", "number", "F0", yesNo, true);

        return v;
    }

    /// <summary>Second-pass rows, by the panel each belongs to.</summary>
    private static readonly List<string> FsCopilotEngineRows = new()
    {
        "DA40_ENG_FIRE", "DA40_ENG_FAILED",
        "DA40_ENG_BLOCK_TEMP", "DA40_ENG_RAD_TEMP", "DA40_ENG_THERMOSTAT",
        "DA40_ENG_GEARBOX_TEMP", "DA40_ENG_COOLANT_LEVEL", "DA40_ENG_COOLANT_LEAK_RATE"
    };

    private static readonly List<string> FsCopilotEcuRows = new()
    {
        "DA40_ECU_A_STARTING", "DA40_ECU_B_STARTING",
        "DA40_ECU_A_FAIL_TIME", "DA40_ECU_B_FAIL_TIME"
    };

    private static readonly List<string> FsCopilotApRows2 = new()
    {
        "DA40_AP_FORCE_AILERON", "DA40_AP_FORCE_ELEVATOR", "DA40_AP_TOGA_ACTIVE"
    };

    private static readonly List<string> FsCopilotStartRows = new()
    {
        "DA40_START_AUTOSTART_STEP", "DA40_START_INPUT"
    };

    private static readonly List<string> FsCopilotDamageRows = new()
    {
        "DA40_DAMAGE_TURBO_FRICTION", "DA40_DAMAGE_FUEL_OSCILLATION"
    };

    private static readonly List<string> FsCopilotFuelFailureRows = new()
    {
        "DA40_FAIL_FUEL_LEFT", "DA40_FAIL_FUEL_RIGHT"
    };

    private static readonly List<string> FsCopilotEngineFailureRows = new()
    {
        "DA40_FAIL_WASTEGATE_A", "DA40_FAIL_WASTEGATE_B"
    };

    // ==================================================================================
    // THIRD PASS: the rest of it, including the ones that look pointless.
    //
    // WARNING: EVERY ENGINE TEMPERATURE IN THIS DEFINITION READS A DISP_ VARIABLE, which
    // is the G1000's DRAWN value and not the measurement. This project already learned that
    // once - "take RPM from PROP_RPM_SENS:1, never DISP_PROP_RPM, the display var is
    // quantised" - and the lesson was applied to RPM ALONE. Oil temperature, coolant
    // temperature, gearbox temperature, oil pressure and cabin-heat source all still come
    // from DISP_OT / DISP_WT / DISP_GT / DISP_OP / DISP_CT. Measured live: DISP_GT against
    // GC_GEARBOX_TEMPERATURE:1 at 80.79.
    //
    // These are ADDED BESIDE the display rows, never swapped in over them, on purpose. A
    // gauge's coloured BAND is looked up from the value the gauge itself carries, so
    // replacing the source under a band lookup would change which arc a reading falls in -
    // a silent behaviour change to the one thing on this aeroplane that says whether a
    // temperature is safe. The two answer different questions and both are now available;
    // which the panels should PREFER is a decision for the pilot, not a refactor to slip in.
    // ==================================================================================

    private static Dictionary<string, SimVarDefinition> BuildFsCopilotThirdPassVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        var yesNo = new Dictionary<double, string> { [0] = "No", [1] = "Yes" };

        // ---------- THE MEASURED TEMPERATURES, BESIDE THE DRAWN ONES ----------
        AddFind(v, "DA40_ENG_OIL_TEMP_ACTUAL", "WC_TEMP_OIL:1", SimVarType.LVar,
            "Oil Temperature Measured", "Celsius, unquantised. The panel row is the drawn value.");
        AddFind(v, "DA40_ENG_WATER_TEMP_ACTUAL", "WC_TEMP_WATER:1", SimVarType.LVar,
            "Coolant Temperature Measured", "Celsius, unquantised.");

        // The SENSORS, which are a third thing again: what the instrument is being TOLD,
        // so a failed sensor shows up as a sensor reading that disagrees with the physical
        // temperature beside it.
        AddFind(v, "DA40_ENG_OIL_TEMP_SENSOR", "WC_TEMP_OIL_SENS:1", SimVarType.LVar,
            "Oil Temperature Sensor", "What the sensor reports, which a failure can bias.");
        AddFind(v, "DA40_ENG_WATER_TEMP_SENSOR", "WC_TEMP_WATER_SENS:1", SimVarType.LVar,
            "Coolant Temperature Sensor", "What the sensor reports.");
        AddFind(v, "DA40_ENG_GEARBOX_TEMP_SENSOR", "WC_TEMP_GC_SENS:1", SimVarType.LVar,
            "Gearbox Temperature Sensor", "What the sensor reports.");
        AddFind(v, "DA40_ENG_FUEL_PRESS_SENSOR", "FUEL_PRESS_SENS:1", SimVarType.LVar,
            "Fuel Pressure Sensor", "What the sensor reports.");

        // ---------- THE RAW DAMAGE ACCUMULATORS ----------
        //
        // WARNING: NOT THE SAME SCALE AS EACH OTHER. The model computes
        // HEALTH_BLOCK = 1 - DAMAGE_BLOCK/800 but HEALTH_OIL = (100 - DAMAGE_OIL)/100, so a
        // completely destroyed block publishes 0.875 health. The health percentages are
        // rescaled for the pilot; these are what the model actually counts.
        AddFind(v, "DA40_DAMAGE_BLOCK_RAW", "DAMAGE_BLOCK:1", SimVarType.LVar,
            "Block Damage", "Raw accumulator. Self-sustaining past 90.");
        AddFind(v, "DA40_DAMAGE_OIL_RAW", "DAMAGE_OIL:1", SimVarType.LVar,
            "Oil Damage", "Raw accumulator. Ignores the damage-enabled switch.");
        AddFind(v, "DA40_DAMAGE_TURBO_RAW", "DAMAGE_TURBO:1", SimVarType.LVar,
            "Turbocharger Damage", "Raw accumulator.");
        AddFind(v, "DA40_DAMAGE_FUEL_RAW", "DAMAGE_FUEL:1", SimVarType.LVar,
            "Fuel System Damage", "Raw accumulator.");
        AddFind(v, "DA40_DAMAGE_FUEL_PUMP_1", "DAMAGE_FUEL:11", SimVarType.LVar,
            "Fuel Pump 1 Damage", "Index 11 is the first pump.");
        AddFind(v, "DA40_DAMAGE_FUEL_PUMP_2", "DAMAGE_FUEL:12", SimVarType.LVar,
            "Fuel Pump 2 Damage", "Index 12 is the second pump.");

        // ---------- FAILURES WITH NO ROW ----------
        AddFind(v, "DA40_FAIL_PROP_COMBINED", "FAILURES_PROP:1", SimVarType.LVar,
            "Propeller Failure", "The combined propeller failure, beside channels A and B.",
            "number", "F0", yesNo, true);
        AddFind(v, "DA40_FAIL_TURBO_STOCK", "RECIP ENG TURBOCHARGER FAILED:1",
            SimVarType.SimVar, "Turbocharger Failed",
            "The stock failure flag, which the sim sets as well as the model.",
            "bool", "F0", yesNo, true);

        // ---------- WHAT THE PILOT IS HOLDING ----------
        AddFind(v, "DA40_TRIM_AXIS_INPUT", "INPUT_TRIM_AXIS", SimVarType.LVar,
            "Trim Axis Input", "The trim axis, as distinct from the resulting trim position.");
        AddFind(v, "DA40_ECU_TEST_HELD", "ECU_TEST:1_IsDown", SimVarType.LVar,
            "ECU Test Button Held", "Whether the button is being held down right now.",
            "number", "F0", yesNo);

        // ---------- THE STOCK SWITCH MIRRORS ----------
        //
        // Bound as the STOCK variables the sim itself keeps, beside the model's own inputs.
        // They are what an external tool sees, and a disagreement between the two is exactly
        // the kind of fault that is otherwise invisible.
        AddFind(v, "DA40_PITOT_HEAT_STOCK", "PITOT HEAT SWITCH:1", SimVarType.SimVar,
            "Pitot Heat Switch", "The stock switch state.", "bool", "F0", yesNo);
        AddFind(v, "DA40_ENGINE_MASTER_STOCK", "RECIP ENG ENGINE MASTER SWITCH:1",
            SimVarType.SimVar, "Engine Master Switch", "The stock switch state.",
            "bool", "F0", yesNo);
        AddFind(v, "DA40_FUEL_PUMP_STOCK", "GENERAL ENG FUEL PUMP SWITCH EX1:1",
            SimVarType.SimVar, "Fuel Pump Switch", "The stock switch state.",
            "bool", "F0", yesNo);

        return v;
    }

    // ⚠️ NO RESET BUTTONS HERE. Four were added and removed the same hour: this definition
    // ALREADY has all six the MFD's Reset Menu offers (DA40_FAIL_RESET, _DAMAGE, _BATT,
    // _ECU, _WIRE, _ALL), and they were invisible to the YAML diff because a BUTTON's Name
    // is its own KEY - the L:var it writes lives in the setter, not in the definition. So
    // "this L:var is not bound as a Name" does NOT mean the aeroplane cannot already do it,
    // and every candidate must also be searched for in the SOURCE before being called
    // missing. Four duplicate reset buttons is what that oversight produced.

    private static readonly List<string> FsCopilotEngineRows2 = new()
    {
        "DA40_ENG_OIL_TEMP_ACTUAL", "DA40_ENG_WATER_TEMP_ACTUAL",
        "DA40_ENG_OIL_TEMP_SENSOR", "DA40_ENG_WATER_TEMP_SENSOR",
        "DA40_ENG_GEARBOX_TEMP_SENSOR", "DA40_ENG_FUEL_PRESS_SENSOR",
        "DA40_ENGINE_MASTER_STOCK", "DA40_FUEL_PUMP_STOCK"
    };

    private static readonly List<string> FsCopilotDamageRows2 = new()
    {
        "DA40_DAMAGE_BLOCK_RAW", "DA40_DAMAGE_OIL_RAW", "DA40_DAMAGE_TURBO_RAW",
        "DA40_DAMAGE_FUEL_RAW", "DA40_DAMAGE_FUEL_PUMP_1", "DA40_DAMAGE_FUEL_PUMP_2"
    };


    private static readonly List<string> FsCopilotTrimRows = new() { "DA40_TRIM_AXIS_INPUT" };
    private static readonly List<string> FsCopilotEcuRows2 = new() { "DA40_ECU_TEST_HELD" };
    private static readonly List<string> FsCopilotIcePitotRows2 = new() { "DA40_PITOT_HEAT_STOCK" };
    private static readonly List<string> FsCopilotEngineFailureRows2 = new()
    {
        "DA40_FAIL_PROP_COMBINED", "DA40_FAIL_TURBO_STOCK"
    };
}
