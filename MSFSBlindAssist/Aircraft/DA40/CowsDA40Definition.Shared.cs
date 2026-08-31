using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Variables the output-mode readouts need that do not belong to any panel built so far.
///
/// A readout hotkey answers from the SimConnect cache, and TWO things about that cache
/// decide what belongs here.
///
/// It is keyed by the MSFSBA VARIABLE KEY, never by the SimVar name — `lastVariableValues`
/// is written as `lastVariableValues[varKey]`. Looking up "FUEL TANK LEFT MAIN QUANTITY"
/// or "KOHLSMAN SETTING HG:1" therefore returns null however correct the name is, and a
/// `?? 0` fallback then answers zero. That is exactly what the B, F and W keys did: they
/// reported "0 hectopascals" and "0.0 gallons" on a full aeroplane.
///
/// And only CONTINUOUS variables are in it at all — an OnRequest variable is polled only
/// while its own panel is open, so a hotkey depending on one answers a stale zero
/// whenever the pilot has not just been looking at that panel. Registering them here makes B and L work now
/// rather than waiting for the G1000 and Flaps panels; when those arrive they reuse these
/// same keys rather than defining second copies.
/// </summary>
public partial class CowsDA40Definition
{
    private static Dictionary<string, SimVarDefinition> BuildSharedReadoutVariables() => new()
    {
        // The G1000's own subscale. The standby one lives on the Standby panel; the B key
        // reads both, because this aeroplane has two and either can be wrong on its own.
        ["DA40_G1000_BARO"] = new SimVarDefinition
        {
            Name = "KOHLSMAN SETTING HG:1",
            DisplayName = "Altimeter Setting",
            Type = SimVarType.SimVar,
            Units = "inHg",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            ExcludeFromMonitorManager = true
        },

        // Tank quantities, for the F readout and for the Fuel System scan. BOTH variants
        // register these — the F key must answer on the XLS too, and the NG-only fuel
        // panel reuses these same keys rather than defining second copies.
        //
        // CONTINUOUS, not OnRequest: a readout hotkey answers from the cache, and an
        // OnRequest variable is only polled while its panel is open. They are numbers, so
        // they stay silent and out of the Monitor Manager.
        ["DA40_FUEL_MAIN_ACTUAL"] = new SimVarDefinition
        {
            Name = "FUEL TANK LEFT MAIN QUANTITY",
            DisplayName = "Main Tank Measured",
            Type = SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F1"
        },

        ["DA40_FUEL_AUX_ACTUAL"] = new SimVarDefinition
        {
            Name = "FUEL TANK RIGHT MAIN QUANTITY",
            DisplayName = "Auxiliary Tank Measured",
            Type = SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F1"
        },

        // Gross weight, for the W readout.
        ["DA40_GROSS_WEIGHT"] = new SimVarDefinition
        {
            Name = "TOTAL WEIGHT",
            DisplayName = "Gross Weight",
            Type = SimVarType.SimVar,
            Units = "pounds",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            ExcludeFromMonitorManager = true
        }

        // DA40_FLAPS_POSITION used to live here for the same reason. It has since been
        // PROMOTED into the Flaps panel, where it is the selector control itself — the
        // key is unchanged, so the L readout and the flap limit speeds still find it.
        // It was never duplicated, and must not be: two keys sharing one SimVar Name are
        // fine in general but NOT when both are Continuous and batched, because the
        // continuous batch sorts by name and a duplicate shifts every later variable's
        // struct slot.
    };
}
