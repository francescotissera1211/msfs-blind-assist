using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Variables the output-mode readouts need that do not belong to any panel built so far.
///
/// A readout hotkey answers from the SimConnect cache, and only Continuous variables are
/// in that cache — an OnRequest variable is not polled, so the key would either say
/// nothing or answer with a stale zero. Registering them here makes B and L work now
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
