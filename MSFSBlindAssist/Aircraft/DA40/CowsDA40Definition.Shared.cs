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
/// And CONTINUOUS ALONE IS NOT ENOUGH. Batch membership — which is what actually gets a
/// variable polled and cached — is `Continuous && IsAnnounced && !ExcludeFromBatch`
/// (SimConnectManager.Setup.cs). A Continuous variable with IsAnnounced false falls to the
/// individual-data-def branch instead, which is only read on request, so it never reaches
/// the cache at all. That is why the B, F and W keys still answered "not available yet"
/// after being pointed at the right KEYS: the keys were right and the variables were never
/// being polled.
///
/// So everything here is IsAnnounced, and silenced instead in ProcessSimVarUpdate, which
/// returns true for these keys so the generic announcer never speaks them. Announcing a
/// tank quantity or a subscale on every change would bury everything else. Registering them here makes B and L work now
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
            IsAnnounced = true,
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
            IsAnnounced = true,
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
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F1"
        },

        // Indicated airspeed. The flap panel's overspeed warning needs it from the cache,
        // and airspeed is otherwise only a FIXED data definition in SimConnectManager -
        // not a keyed variable, so there is nothing in lastVariableValues to look up.
        ["DA40_AIRSPEED"] = new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Airspeed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true
        },

        // Gross weight, for the W readout.
        ["DA40_GROSS_WEIGHT"] = new SimVarDefinition
        {
            Name = "TOTAL WEIGHT",
            DisplayName = "Gross Weight",
            Type = SimVarType.SimVar,
            Units = "pounds",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
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

    /// <summary>
    /// Readout plumbing that must be POLLED but must never be SPOKEN.
    ///
    /// These have to be IsAnnounced to earn a place in the continuous batch - that is the
    /// only thing that gets them cached - so the silence has to come from somewhere else,
    /// and ProcessSimVarUpdate is where. Every one is a NUMBER that changes constantly.
    /// </summary>
    public static IReadOnlyCollection<string> SilentCachedReadoutKeys => SilentCachedReadouts;

    private static readonly HashSet<string> SilentCachedReadouts = new()
    {
        "DA40_G1000_BARO",
        "DA40_FUEL_MAIN_ACTUAL",
        "DA40_FUEL_AUX_ACTUAL",
        "DA40_GROSS_WEIGHT",
        "DA40_AIRSPEED",
        "DA40_TRIM_SET",
        "DA40_POWER_LEVER_SET"
    };

    /// <summary>
    /// Returning true means "handled" - the generic announcer never runs for that key.
    /// Nothing is announced here; that IS the handling.
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (SilentCachedReadouts.Contains(varName)) return true;

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }
}
