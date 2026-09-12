using System.Collections.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Simulation - the XLS's own Engine Failures, Fuel Failures, Engine Damage and Breaker
/// Trips. XLS only.
///
/// ⚠️ THE XLS HAD NO ENGINE FAILURE PANELS AT ALL. GetPanelStructure REMOVED "Engine
/// Failures", "Fuel Failures", "FADEC and Sensors" and "Engine Damage" for this variant -
/// correctly for the FADEC one, since a Lycoming has no ECU, and WRONGLY for the other
/// three. The Lycoming is failed in MORE ways than the Austro, not fewer: per-cylinder
/// failures, per-PLUG magneto failures, a magneto that can stay LIVE when grounded, three
/// control levers that can each come adrift, and its own fuel and oil failures. None of it
/// was reachable from MSFSBA.
///
/// Every name here was read out of the XLS model directory and carries its exact index.
/// Found by diffing FS Copilot's COWS_DA40XLS.yaml against our own variable surface -
/// enumerated from the BUILT ASSEMBLY, never source text - and then checking each candidate
/// against the package. That second step is what separates these from the eighteen FS
/// Copilot names the XLS does not have at all.
///
/// ⚠️ EVERY FAILURE IS ANNOUNCED. A failure the pilot is not told about is one they find out
/// about from the aeroplane's behaviour, which is the thing this app exists to prevent.
/// </summary>
public partial class CowsDA40Definition
{
    private static readonly Dictionary<double, string> XlsFailedStates = new()
    {
        [0] = "Normal",
        [1] = "FAILED"
    };

    private static Dictionary<string, SimVarDefinition> BuildXlsFailureVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Per-cylinder ----------
        for (int c = 1; c <= 4; c++)
        {
            XlsFail(v, $"DA40_XLS_FAIL_CYL_{c}", $"FAILURES_CYL:{c}",
                $"Cylinder {c}", "The cylinder itself fails.");
            XlsFail(v, $"DA40_XLS_FAIL_INJ_{c}", $"FAILURES_FUEL_INJ:{c}",
                $"Cylinder {c} Injector", "A blocked injector leans that cylinder alone.");
            XlsFail(v, $"DA40_XLS_FAIL_CHT_OIL_{c}", $"FAILURES_CHT_OIL:{c}",
                $"Cylinder {c} Oil Cooling", "Oil cooling to one cylinder.");

            // ⚠️ TWO PLUGS PER CYLINDER, LEFT AND RIGHT - which is exactly what a magneto
            // drop tests, and why the mag check is spoken as numbers on this aeroplane.
            XlsFail(v, $"DA40_XLS_FAIL_MAG_{c}L", $"FAILURES_MAG:{c}L",
                $"Cylinder {c} Left Plug", "One spark plug. A mag check finds it as a rough drop.");
            XlsFail(v, $"DA40_XLS_FAIL_MAG_{c}R", $"FAILURES_MAG:{c}R",
                $"Cylinder {c} Right Plug", "One spark plug. A mag check finds it as a rough drop.");

            XlsReadout(v, $"DA40_XLS_CYL_HEALTH_{c}", $"HEALTH_CYL:{c}",
                $"Cylinder {c} Health", "1.0 is undamaged.");
            XlsFlag(v, $"DA40_XLS_CYL_DEAD_{c}", $"KAPUTT_CYL:{c}",
                $"Cylinder {c} Destroyed", "Past saving.");
            XlsReadout(v, $"DA40_XLS_CYL_DAMAGE_FAC_{c}", $"DAMAGE_CYL_FAC:{c}",
                $"Cylinder {c} Damage Rate", "How fast this cylinder is being damaged.");
        }

        // ---------- Magnetos, whole side ----------
        XlsFail(v, "DA40_XLS_FAIL_MAG_LEFT", "FAILURES_MAG_L", "Left Magneto",
            "The whole left magneto.");
        XlsFail(v, "DA40_XLS_FAIL_MAG_RIGHT", "FAILURES_MAG_R", "Right Magneto",
            "The whole right magneto.");

        // ⚠️ A MAGNETO THAT WILL NOT GROUND IS LIVE WITH THE KEY OFF - the hand-propping
        // killer, and the one failure here that matters before the engine is even running.
        XlsFail(v, "DA40_XLS_FAIL_MAG_GND_L", "FAILURES_MAG_GND_L", "Left Magneto Grounding",
            "Failed grounding leaves that magneto LIVE with the key off.");
        XlsFail(v, "DA40_XLS_FAIL_MAG_GND_R", "FAILURES_MAG_GND_R", "Right Magneto Grounding",
            "Failed grounding leaves that magneto LIVE with the key off.");

        // ---------- The three levers ----------
        XlsFail(v, "DA40_XLS_FAIL_THROT_LEVER", "FAILURES_THROT_LEVER", "Throttle Lever",
            "The lever comes adrift and stops commanding the engine.");
        XlsFail(v, "DA40_XLS_FAIL_PROP_LEVER", "FAILURES_PROP_LEVER", "Propeller Lever",
            "The lever comes adrift and stops commanding the governor.");
        XlsFail(v, "DA40_XLS_FAIL_MIX_LEVER", "FAILURES_MIX_LEVER", "Mixture Lever",
            "The lever comes adrift and stops commanding mixture.");

        // ---------- Engine, whole ----------
        XlsFail(v, "DA40_XLS_FAIL_BLOCK", "FAILURES_BLOCK", "Engine Block", "");
        XlsFail(v, "DA40_XLS_FAIL_OIL", "FAILURES_OIL", "Oil System", "");
        XlsFail(v, "DA40_XLS_FAIL_BYPASS", "FAILURES_BYPASS", "Oil Bypass", "");
        XlsFail(v, "DA40_XLS_FAIL_THERMOSTAT", "FAILURES_THERMOSTAT_OIL", "Oil Thermostat", "");
        XlsFail(v, "DA40_XLS_FAIL_CHT_BAFFLE", "FAILURES_CHT_BAFFLE", "Cooling Baffle",
            "Cylinder cooling airflow.");
        XlsFail(v, "DA40_XLS_FAIL_VACC_LEAK", "FAILURES_VACC_LEAK", "Induction Leak", "");
        XlsFail(v, "DA40_XLS_FAIL_PROP_PUMP", "FAILURES_PROP_PUMP",
            "Propeller Governor Pump", "");
        XlsFail(v, "DA40_XLS_FAIL_ALT_OVERVOLT", "FAILURES_ALT_OVERVOLT",
            "Alternator Overvoltage", "");

        XlsReadout(v, "DA40_XLS_BLOCK_DAMAGE_FAC", "DAMAGE_BLOCK_FAC", "Block Damage Rate",
            "How fast the block is being damaged.");

        // ---------- Fuel ----------
        XlsFail(v, "DA40_XLS_FAIL_FUEL_PUMP", "FAILURES_FUEL_PUMP", "Electric Fuel Pump", "");
        XlsFail(v, "DA40_XLS_FAIL_FUEL_SPRING", "FAILURES_FUEL_SPRING",
            "Fuel Pressure Spring", "");
        XlsFail(v, "DA40_XLS_FAIL_FUEL_LEAK", "FAILURES_FUEL_LEAK", "Fuel Leak", "");
        XlsFail(v, "DA40_XLS_FAIL_FUEL_LEAK_L", "FAILURES_FUEL_LEAK_L", "Left Tank Leak", "");
        XlsFail(v, "DA40_XLS_FAIL_FUEL_LEAK_R", "FAILURES_FUEL_LEAK_R", "Right Tank Leak", "");

        // ---------- Breaker trips ----------
        // The six the XLS carries that had no row. Its trip list is longer than this; the
        // rest already have one.
        XlsFail(v, "DA40_XLS_TRIP_ADF", "FAILURES_CB_ADF", "ADF Breaker Trip", "");
        XlsFail(v, "DA40_XLS_TRIP_ALT_CONT", "FAILURES_CB_ALT_CONT",
            "Alternator Control Breaker Trip", "");
        XlsFail(v, "DA40_XLS_TRIP_ALT_PROT", "FAILURES_CB_ALT_PROT",
            "Alternator Protection Breaker Trip", "");
        XlsFail(v, "DA40_XLS_TRIP_AV_BUS", "FAILURES_CB_AV_BUS",
            "Avionics Bus Breaker Trip", "");
        XlsFail(v, "DA40_XLS_TRIP_BATT", "FAILURES_CB_BATT", "Battery Breaker Trip", "");
        XlsFail(v, "DA40_XLS_TRIP_FUEL_PUMP", "FAILURES_CB_FUEL_PUMP",
            "Fuel Pump Breaker Trip", "");

        return v;
    }

    /// <summary>
    /// A failure: announced, settable, and named as a STATE rather than a yes/no - the same
    /// shape every other failure on this aeroplane uses.
    /// </summary>
    private static void XlsFail(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display, string help)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            Format = "F0",
            ValueDescriptions = new Dictionary<double, string>(XlsFailedStates),
            HelpText = help
        };
    }

    /// <summary>A silent flag: a state a pilot looks up rather than one that interrupts.</summary>
    private static void XlsFlag(Dictionary<string, SimVarDefinition> v, string key,
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
            Format = "F0",
            ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "YES" },
            HelpText = help
        };
    }

    /// <summary>A number whose unit the package never names - read as a bare figure.</summary>
    private static void XlsReadout(Dictionary<string, SimVarDefinition> v, string key,
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
            Format = "F2",
            HelpText = help
        };
    }

    /// <summary>Engine Failures, XLS. Per-cylinder first, then the whole-engine ones.</summary>
    private static List<string> XlsEngineFailureControls()
    {
        var l = new List<string>();
        for (int c = 1; c <= 4; c++)
        {
            l.Add($"DA40_XLS_FAIL_CYL_{c}");
            l.Add($"DA40_XLS_FAIL_INJ_{c}");
            l.Add($"DA40_XLS_FAIL_MAG_{c}L");
            l.Add($"DA40_XLS_FAIL_MAG_{c}R");
            l.Add($"DA40_XLS_FAIL_CHT_OIL_{c}");
        }

        l.AddRange(new[]
        {
            "DA40_XLS_FAIL_MAG_LEFT", "DA40_XLS_FAIL_MAG_RIGHT",
            "DA40_XLS_FAIL_MAG_GND_L", "DA40_XLS_FAIL_MAG_GND_R",
            "DA40_XLS_FAIL_THROT_LEVER", "DA40_XLS_FAIL_PROP_LEVER",
            "DA40_XLS_FAIL_MIX_LEVER",
            "DA40_XLS_FAIL_BLOCK", "DA40_XLS_FAIL_OIL", "DA40_XLS_FAIL_BYPASS",
            "DA40_XLS_FAIL_THERMOSTAT", "DA40_XLS_FAIL_CHT_BAFFLE",
            "DA40_XLS_FAIL_VACC_LEAK", "DA40_XLS_FAIL_PROP_PUMP",
            "DA40_XLS_FAIL_ALT_OVERVOLT"
        });
        return l;
    }

    private static List<string> XlsFuelFailureControls() => new()
    {
        "DA40_XLS_FAIL_FUEL_PUMP",
        "DA40_XLS_FAIL_FUEL_SPRING",
        "DA40_XLS_FAIL_FUEL_LEAK",
        "DA40_XLS_FAIL_FUEL_LEAK_L",
        "DA40_XLS_FAIL_FUEL_LEAK_R"
    };

    /// <summary>
    /// Engine Damage, XLS: per-cylinder health and how fast it is being lost. These are
    /// READOUTS, not controls - nothing here can be set - so they go in the panel's
    /// DISPLAY rows, exactly as the NG's own health readings do. A read-only row placed in
    /// the CONTROLS list reads as a switch that will not announce a background change.
    /// </summary>
    private static List<string> XlsDamageDisplay()
    {
        var l = new List<string>();
        for (int c = 1; c <= 4; c++)
        {
            l.Add($"DA40_XLS_CYL_HEALTH_{c}");
            l.Add($"DA40_XLS_CYL_DEAD_{c}");
            l.Add($"DA40_XLS_CYL_DAMAGE_FAC_{c}");
        }
        l.Add("DA40_XLS_BLOCK_DAMAGE_FAC");
        return l;
    }

    private static List<string> XlsBreakerTripControls() => new()
    {
        "DA40_XLS_TRIP_ADF",
        "DA40_XLS_TRIP_ALT_CONT",
        "DA40_XLS_TRIP_ALT_PROT",
        "DA40_XLS_TRIP_AV_BUS",
        "DA40_XLS_TRIP_BATT",
        "DA40_XLS_TRIP_FUEL_PUMP"
    };
}
