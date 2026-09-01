using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Flight Controls. Both variants.
///
/// THIS PANEL EXISTS FOR ONE CHECKLIST ITEM, and it is one a blind pilot could not do at
/// all. "Flight controls . . . CHECKED" appears in BEFORE TAKE OFF CHECK on the
/// aeroplane's own electronic checklist, and it means what it means on every light
/// aeroplane: move the stick and the pedals through their FULL travel and confirm the
/// surfaces follow, freely, in the right direction. A sighted pilot does that by looking
/// out at the ailerons and feeling the stops. There was no way to do it here at all —
/// nothing in MSFSBA reported where any control surface was.
///
/// So this is a READOUT PANEL and nothing else: three surfaces, live, on the scan. The
/// pilot sweeps the stick and hears the numbers run to their stops. There are no controls
/// on it, deliberately — the aeroplane is flown with a stick and pedals, and offering
/// buttons that drive the surfaces would be inventing a cockpit that does not exist.
///
/// SURFACE POSITION, NOT INPUT POSITION. The COWS model publishes L:INPUT_ELEVATOR and
/// L:INPUT_AILERON, which are where the pilot's stick is. This panel reads the stock
/// ELEVATOR / AILERON / RUDDER POSITION SimVars instead, which are where the SURFACES
/// are, because that is the difference the check is FOR: a jammed or disconnected control
/// moves the stick and not the surface, and reading the input back would report the check
/// as passing in exactly the case it exists to catch.
///
/// The SimVars span -1 to +1 and are reported as a percentage with the direction named.
/// A bare "-100" leaves the pilot to remember which way the sign runs, and on the elevator
/// that is the one convention everybody gets backwards; the trim panel says the same.
/// </summary>
public partial class CowsDA40Definition
{
    private const string FlightControlsPanel = "Flight Controls";

    private static Dictionary<string, SimVarDefinition> BuildFlightControlVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddSurface(v, "DA40_CTL_ELEVATOR", "ELEVATOR POSITION", "Elevator");
        AddSurface(v, "DA40_CTL_AILERON", "AILERON POSITION", "Ailerons");
        AddSurface(v, "DA40_CTL_RUDDER", "RUDDER POSITION", "Rudder");

        return v;
    }

    /// <summary>
    /// A control-surface readout. Continuous and announced so it reaches the shared batch
    /// cache — the batch takes a variable only when it is Continuous AND IsAnnounced —
    /// then silenced in ProcessSimVarUpdate, because a control check sweeps the stick and
    /// announcing every intermediate position would be a torrent over the one moment the
    /// pilot is concentrating.
    /// </summary>
    private static void AddSurface(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "position",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };
    }

    private static readonly List<string> FlightControlDisplay = new()
    {
        "DA40_CTL_ELEVATOR",
        "DA40_CTL_AILERON",
        "DA40_CTL_RUDDER"
    };

    /// <summary>
    /// One surface as a percentage with its direction named. Rendered here rather than
    /// left to the generic formatter, which knows nothing about this unit and would put a
    /// bare signed fraction on the scan.
    /// </summary>
    private bool TryGetFlightControlDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";

        string low, high;
        switch (varKey)
        {
            case "DA40_CTL_ELEVATOR": low = "nose down"; high = "nose up"; break;
            case "DA40_CTL_AILERON": low = "left wing down"; high = "right wing down"; break;
            case "DA40_CTL_RUDDER": low = "left"; high = "right"; break;
            default: return false;
        }

        int pct = (int)Math.Round(Math.Abs(value) * 100.0);
        displayText = pct == 0
            ? "centred"
            : $"{pct} percent {(value > 0 ? high : low)}"
              + (pct >= 99 ? ", at the stop" : "");
        return true;
    }
}
