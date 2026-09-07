using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Power and Levers (DA40-XLS).
///
/// The Lycoming IO-360 pedestal: THREE levers — throttle, propeller, mixture — where the
/// NG has one. The panel name is shared with the NG's (it is the same place in the
/// cockpit); the keys are not, because the arcs and the meanings differ, and a shared key
/// would hand the XLS the Austro's 2300-rpm red line.
///
/// Every unit and every write path here was measured on the live aircraft through a cold
/// start and a run-up (docs/da40-xls-variables.md). What the variable list does not show:
///
///  • THE THROTTLE HAS NO WRITABLE VARIABLE. <c>L:THROTTLE_LEVER</c> is a read-only mirror
///    (stock position ÷ 100) and a write snaps back. The lever is driven through the stock
///    axis event and read back from the stock position — and COWS trims what is commanded
///    (12 % commanded read back 9.2 %), so the readback is the truth and the typed number
///    is a request.
///  • <c>INPUT_PROPELLER</c> and <c>INPUT_MIXTURE</c> are the vendor's documented inputs
///    (DA40 LVAR bindings.txt, 0–100), writable and holding. The stock propeller-lever
///    simvar is a COWS intermediate (0 % with the lever at 100, 54 % with it at 0) and the
///    stock mixture lever is rewritten by COWS every tick: neither is read here.
///  • The propeller lever maps LINEARLY onto the governor's target: <c>OP_PROP_TARGET_RPM</c>
///    runs from <c>PROP_SPREAD_LO</c> at 0 to <c>PROP_SPREAD_HI</c> at 100 — this engine's
///    1470 to 2676, and those two numbers are per-engine build variation, so the target is
///    read, never computed.
///  • Manifold pressure is <c>TB_CALC_MAP</c> in BAR. The G1000 draws it ×29.53 as inHg
///    (<c>DISP_MAP</c> matched to the hundredth), so the row is scaled the same way. The
///    stock manifold-pressure simvar is the different value COWS injects for engine output
///    and reads ~2 inHg off; the stock EGT simvar is never written at all. Neither is used.
///  • The tachometer is the stock <c>GENERAL ENG RPM:1</c>, which COWS feeds once the engine
///    is above the MSFS 400-rpm floor. It is ONE batched key, owned here and shared with the
///    Magnetos panel, because two batched keys on one SimVar name corrupt the whole batch.
///  • <c>EGT_MIXTURE</c> is the AIR/FUEL RATIO — 10.35 at full rich, and the POH leans by it
///    (10:1 rich, 12.5:1 best power, 14.7:1 stoichiometric). It is the number a sighted pilot
///    infers from the EGT bars, so it is on the scan.
///
/// The three levers are NUMBERS and do not announce themselves: under hardware they would
/// speak a new percentage several times a second. They are cached silently so the
/// readout hotkeys can answer, and a typed entry confirms once. Switches announce; values
/// are read.
/// </summary>
public partial class CowsDA40Definition
{
    private static Dictionary<string, SimVarDefinition> BuildXlsPowerVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_XLS_THROTTLE_SET"] = new SimVarDefinition
        {
            Name = "GENERAL ENG THROTTLE LEVER POSITION:1",
            DisplayName = "Throttle",
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true,
            Format = "F0",
            HelpText = "0 to 100 percent. Sets manifold pressure. The aeroplane trims what you type; the row shows where the lever really is."
        };

        v["DA40_XLS_PROP_SET"] = new SimVarDefinition
        {
            Name = "INPUT_PROPELLER",
            DisplayName = "Propeller Lever",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true,
            Format = "F0",
            HelpText = "0 coarse to 100 fine. Full forward for take-off and landing; the governor target row says what RPM it is asking for."
        };

        v["DA40_XLS_MIXTURE_SET"] = new SimVarDefinition
        {
            Name = "INPUT_MIXTURE",
            DisplayName = "Mixture",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true,
            Format = "F0",
            HelpText = "0 idle cut-off to 100 full rich. Full rich is 10 to 1; lean until the air-to-fuel ratio row reads what you want."
        };

        // ---------- Status ----------

        // The one batched tachometer on the XLS. Shared with the Magnetos panel, which reads
        // the drop from it; never spoken on its own.
        v["DA40_XLS_RPM"] = new SimVarDefinition
        {
            Name = "GENERAL ENG RPM:1",
            DisplayName = "RPM",
            Type = SimVarType.SimVar,
            Units = "rpm",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        v["DA40_XLS_MAP"] = new SimVarDefinition
        {
            Name = "TB_CALC_MAP",
            DisplayName = "Manifold Pressure",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            // The variable is BAR. It is rendered as inHg - the conversion the G1000 itself
            // applies - in TryGetDisplayOverride, never via Scale: Scale is applied only when
            // MainForm paints a row, and the hotkeys format straight from the cache.
            Units = "inHg",
            Format = "F1"
        };

        v["DA40_XLS_TARGET_RPM"] = new SimVarDefinition
        {
            Name = "OP_PROP_TARGET_RPM",
            DisplayName = "Governor Target",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Units = "rpm",
            Format = "F0"
        };

        v["DA40_XLS_FUEL_FLOW"] = new SimVarDefinition
        {
            Name = "TB_FUEL_FLOW_GPH",
            DisplayName = "Fuel Flow",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Units = "gallons per hour",
            Format = "F1"
        };

        v["DA40_XLS_OIL_PRESSURE"] = new SimVarDefinition
        {
            Name = "GENERAL ENG OIL PRESSURE:1",
            DisplayName = "Oil Pressure",
            Type = SimVarType.SimVar,
            Units = "psi",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        v["DA40_XLS_OIL_TEMP"] = new SimVarDefinition
        {
            Name = "GENERAL ENG OIL TEMPERATURE:1",
            DisplayName = "Oil Temperature",
            Type = SimVarType.SimVar,
            Units = "celsius",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F0"
        };

        v["DA40_XLS_AFR"] = new SimVarDefinition
        {
            Name = "EGT_MIXTURE",
            DisplayName = "Air to Fuel Ratio",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F1"
        };

        return v;
    }

    private static readonly List<string> XlsPowerControls = new()
    {
        "DA40_XLS_THROTTLE_SET",
        "DA40_XLS_PROP_SET",
        "DA40_XLS_MIXTURE_SET"
    };

    // The run-up scan, in the order the Diamond checklist reads the EIS: what it is
    // making, what it is turning, what it is asking for, what it is burning, oil, mixture.
    private static readonly List<string> XlsPowerDisplay = new()
    {
        "DA40_XLS_MAP",
        "DA40_XLS_RPM",
        "DA40_XLS_TARGET_RPM",
        "DA40_XLS_FUEL_FLOW",
        "DA40_XLS_OIL_PRESSURE",
        "DA40_XLS_OIL_TEMP",
        "DA40_XLS_AFR"
    };

    /// <summary>Bar to inches of mercury - the conversion the G1000 itself applies to DISP_MAP.</summary>
    private const double BarToInHg = 29.53;

    /// <summary>
    /// The two XLS readouts the generic renderer would get wrong: manifold pressure is
    /// stored in bar and would be labelled inHg unconverted, and the air/fuel ratio has no
    /// unit and would lose its decimal. Both paths - the panel row and the hotkeys - come
    /// through here, which is why the conversion is not left to Scale.
    /// </summary>
    private static bool TryGetXlsPowerDisplayOverride(string varKey, double value, out string displayText)
    {
        switch (varKey)
        {
            case "DA40_XLS_MAP":
                displayText = $"{value * BarToInHg:F1} inHg";
                return true;

            case "DA40_XLS_AFR":
                displayText = $"{value:F1} to 1";
                return true;
        }

        displayText = string.Empty;
        return false;
    }

    /// <summary>
    /// The three levers. Throttle goes to the stock axis event (0–16383) because there is
    /// nothing else that moves it; the other two go to their input variables through the
    /// calculator path, uniquified — the same position twice running is a byte-identical
    /// string and the second would be dropped. A typed numeric entry confirms once.
    /// </summary>
    private bool HandleXlsPowerSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        double pct = Math.Clamp(value, 0, 100);

        switch (varKey)
        {
            case "DA40_XLS_THROTTLE_SET":
                int axis = (int)Math.Round(pct / 100.0 * ThrottleAxisMax);
                simConnect.ExecuteCalculatorCode($"{axis} (>K:THROTTLE1_SET)");
                announcer.AnnounceImmediate($"Throttle {pct:0} percent");
                return true;

            case "DA40_XLS_PROP_SET":
                simConnect.ExecuteCalculatorCodeUnique($"{pct:0} (>L:INPUT_PROPELLER)");
                announcer.AnnounceImmediate($"Propeller {pct:0} percent");
                return true;

            case "DA40_XLS_MIXTURE_SET":
                simConnect.ExecuteCalculatorCodeUnique($"{pct:0} (>L:INPUT_MIXTURE)");
                announcer.AnnounceImmediate(pct <= 0 ? "Mixture idle cut-off"
                    : pct >= 100 ? "Mixture full rich"
                    : $"Mixture {pct:0} percent");
                return true;
        }

        return false;
    }
}
