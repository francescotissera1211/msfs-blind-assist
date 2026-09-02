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
            IsAnnounced = true
            // NOT excluded from the Monitor Manager any more: it announces now (debounced,
            // once the knob settles), so its Ctrl+M row mutes something real. The rule is
            // that a checkbox which silences nothing should not exist - the converse is
            // that one which does, must.
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

        // THE TRANSPONDER, announced. It is tuned on the G1000 and nowhere else, so
        // without this the only way to know a squawk changed is to have the PFD window
        // open on the right page. A code is not a continuously-varying quantity - it
        // changes when someone sets it, and ATC assigning one is exactly the background
        // change worth hearing - so it announces, like the standby subscale and unlike the
        // power lever.
        ["DA40_XPDR_CODE"] = new SimVarDefinition
        {
            Name = "TRANSPONDER CODE:1",
            DisplayName = "Squawk",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            Format = "F0"
        },

        ["DA40_XPDR_MODE"] = new SimVarDefinition
        {
            Name = "TRANSPONDER STATE:1",
            DisplayName = "Transponder",
            Type = SimVarType.SimVar,
            Units = "enum",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Standby",
                [2] = "Test",
                [3] = "On",
                [4] = "Altitude reporting"
            }
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
    /// <summary>
    /// EVERYTHING that is polled and never spoken, from both lists.
    ///
    /// It is the union rather than one list because the two have different reasons - these
    /// are readout plumbing, the others are hotkey plumbing - but only one truthful answer
    /// to "will this speak", and the tests that ask it must get that one.
    /// </summary>
    public static IReadOnlyCollection<string> SilentCachedReadoutKeys =>
        SilentCachedReadouts.Concat(HotkeyCachedReadouts).ToList();

    private static readonly HashSet<string> SilentCachedReadouts = new()
    {
        // The control surfaces. Announced only so they reach the batch cache; a control
        // check sweeps the stick stop to stop, and speaking every position on the way
        // would bury the one reading the pilot is listening for.
        "DA40_CTL_ELEVATOR",
        "DA40_CTL_AILERON",
        "DA40_CTL_RUDDER",

        // DA40_G1000_BARO is deliberately NOT here any more. It was, and that is why a
        // subscale changed on external hardware said nothing at all. It is handled by the
        // settle-timer announcer instead, which speaks the value the knob came to rest on.
        "DA40_FUEL_MAIN_ACTUAL",
        "DA40_FUEL_AUX_ACTUAL",
        "DA40_GROSS_WEIGHT",
        "DA40_AIRSPEED",
        "DA40_TRIM_SET",
        "DA40_POWER_LEVER_SET",

        // The flight director bars. They carry IsAnnounced only to reach the batch cache
        // - the whole AP value family does - but the commanded attitude moves continuously
        // in flight, and a pilot flying the bars reads them from the scan, not from a
        // running commentary of every tenth of a degree.
        "DA40_AP_FD_PITCH",
        "DA40_AP_FD_BANK",

        // Carries the waypoint-passing call's Ctrl+M row and nothing else. The call itself is
        // spoken from the GPS waypoint callback, which checks this key's mute for itself.
        "DA40_WAYPOINT_PASSING",

        // The G1000's VNAV output, polled for the Shift+D readout and never spoken on its own.
        "DA40_VNAV_TOD_DIST",
        "DA40_VNAV_PATH_AVAIL"
    };

    /// <summary>
    /// Every hotkey-cached readout is silent for the same reason the list above is: they
    /// carry IsAnnounced only to reach the batch, and they are engine numbers that move
    /// several times a second. Kept as a separate list so the two reasons stay separate,
    /// and unioned here so neither can be forgotten.
    /// </summary>
    private static bool IsSilentCachedReadout(string varName)
        => SilentCachedReadouts.Contains(varName)
           || HotkeyCachedReadouts.Contains(varName);

    /// <summary>
    /// Returning true means "handled" - the generic announcer never runs for that key.
    /// Nothing is announced here; that IS the handling.
    /// </summary>
    public override bool ProcessSimVarUpdate(string varName, double value,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (IsSilentCachedReadout(varName)) return true;

        // A door is OPEN or CLOSED, never "65.4" - the percentage sweeps as the canopy
        // swings and used to announce every step of it.
        if (NoteDoorChange(varName, value, announcer)) return true;

        // Engine health, which falls rather than switching. Only a material fall speaks.
        if (NoteEngineHealth(varName, value, announcer)) return true;

        // The three graded failures - percentages, so the numeric-silence rule was keeping
        // a coolant leak and a turbo failure quiet. Onset and worsening only.
        if (NoteGradedFailure(varName, value, announcer)) return true;

        // A LAMP never speaks on its own; a SWITCH always does. NoteLampChange arms the
        // settle for both and returns true only for the lamp, so the switch carries on to
        // the generic announcer exactly as it always has.
        if (NoteLampChange(varName, announcer)) return true;

        // Both barometric subscales: recorded and announced once the knob settles, rather
        // than on every 0.01 inHg step. Returns true either way - the generic announcer
        // must not also read them.
        if (NoteBaroChange(varName, value, announcer)) return true;

        // Every COM and NAV frequency, active and standby, announced once tuning settles
        // rather than on every 25 kHz step - and spoken from what the RADIO reported,
        // never from a prediction made before the event was sent.
        if (NoteRadioChange(varName, value, announcer)) return true;

        // Remember every breaker position so the per-panel "how many are out" row can be
        // computed. A display override gets no SimConnect, so it cannot read them itself.
        // Returning FALSE here on purpose: a breaker moving on its own is exactly the kind
        // of background change that must still be announced.
        if (varName.StartsWith("DA40_CB_")) _breakerState[varName] = value;

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }
}
