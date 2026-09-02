using System;
using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// "Report passing WAYPOINT" — the one IFR instruction this aeroplane could not answer.
///
/// ⚠️ CORRECTION TO SOMETHING THIS PROJECT WROTE DOWN AS TRUE: the docs said Ctrl+W answered
/// the waypoint on demand and that only the automatic call was missing. It did not. Ctrl+W maps
/// to <c>HotkeyAction.ReadNDWaypoint</c>, which the DA40 never handled, and the shared service
/// behind it reads <c>A32NX_EFIS_L_TO_WPT_*</c> — FlyByWire variables that do not exist on a
/// Diamond. The key did nothing at all, and the audit reported it as working. Both halves were
/// missing, not one.
///
/// WHAT FILLS THEM is the aeroplane's own navigator. The Working Title G1000 carries a
/// <c>GpsSynchronizer</c> that writes the stock GPS SimVars from its active flight plan — read
/// live out of the running instrument rather than taken from documentation — so the TO-waypoint,
/// the waypoint just passed, the distance, the bearing and the time are all published already.
/// Nothing needed to be inferred and nothing needed a Coherent socket.
///
/// ⚠️ MSFSBA ANNOUNCES THE PASSING; IT DOES NOT MAKE THE CALL. Which report to make, and when,
/// is the pilot's job — this only removes the reason they could not know. See
/// <see cref="GpsWaypointSequencer"/> for why "the ident changed" is not the test.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>
    /// The Ctrl+M row, and nothing else. A monitor row needs a real variable behind it, so this
    /// one rides GPS IS ACTIVE FLIGHT PLAN — a genuine reading (is there a plan at all), cheap,
    /// and ⚠️ NOT a SimVar name any other DA40 key already carries: the continuous batch sorts
    /// by SimVar NAME, so a second key on a name already in the batch shifts every later
    /// variable's slot and corrupts unrelated readouts. It is silenced in ProcessSimVarUpdate
    /// via SilentCachedReadouts; the passing call is spoken from the SimConnect callback below,
    /// which checks this key's mute itself.
    /// </summary>
    private static void AddWaypointMonitorRow(Dictionary<string, SimVarDefinition> v)
    {
        v[WaypointMonitorKey] = new SimVarDefinition
        {
            Name = "GPS IS ACTIVE FLIGHT PLAN",
            DisplayName = "Waypoint Passing Call",
            Type = SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            HelpText = "Untick to stop the call announcing when a waypoint sequences."
        };
    }

    /// <summary>
    /// The G1000's own VNAV output, for the top-of-descent readout.
    ///
    /// ⚠️ BOTH NAMES WERE READ OUT OF THE RUNNING INSTRUMENT'S PUBLISHER TABLE, never guessed —
    /// `vNavPublisher` maps `vnav_tod_distance` to `L:WTAP_VNav_Distance_To_TOD` and
    /// `vnav_path_available` to `L:WTAP_VNav_Path_Available`. The flag is not optional: without
    /// a computed path there IS no top of descent, and the distance reads 0 in that case exactly
    /// as it does when the aeroplane is sitting on top of one.
    ///
    /// ⚠️ THE DISTANCE UNIT IS METRES BY THE WORKING TITLE CONVENTION AND IS NOT YET VERIFIED
    /// IN FLIGHT. If it turns out to be nautical miles the readout will be wrong by a factor of
    /// 1852, which is loud rather than subtle — but do not treat it as confirmed until a real
    /// descent has been flown against it.
    ///
    /// Registered `Units = "number"` because they are L:VARS: a converting unit makes SimConnect
    /// convert from a base unit the variable does not have. Announced only to reach the batch
    /// cache, and silenced in ProcessSimVarUpdate.
    /// </summary>
    private static void AddVnavReadouts(Dictionary<string, SimVarDefinition> v)
    {
        Add("DA40_VNAV_TOD_DIST", "WTAP_VNav_Distance_To_TOD", "Distance to Top of Descent");
        Add("DA40_VNAV_PATH_AVAIL", "WTAP_VNav_Path_Available", "Vertical Path Available");

        void Add(string key, string lvar, string label)
        {
            v[key] = new SimVarDefinition
            {
                Name = lvar,
                DisplayName = label,
                Type = SimVarType.LVar,
                Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = true,
                RenderAsReadOnlyStatus = true
            };
        }
    }

    private ScreenReaderAnnouncer? _wptAnnouncer;
    private SimConnectManager? _wptSimConnect;
    private string? _wptLastNextId;

    /// <summary>The Ctrl+M row that silences the passing call. The readout key is never muted.</summary>
    private const string WaypointMonitorKey = "DA40_WAYPOINT_PASSING";

    public void AttachWaypointSequencer(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        StopWaypointSequencer();
        _wptSimConnect = simConnect;
        _wptAnnouncer = announcer;
        simConnect.GpsWaypointReceived += OnGpsWaypoint;
    }

    private void StopWaypointSequencer()
    {
        if (_wptSimConnect != null) _wptSimConnect.GpsWaypointReceived -= OnGpsWaypoint;
        _wptSimConnect = null;
        _wptAnnouncer = null;
        _wptLastNextId = null;
    }

    private void OnGpsWaypoint(object? sender, SimConnectManager.GpsWaypointData data)
    {
        try
        {
            var reading = GpsWaypointSequencer.Read(data, _wptLastNextId);
            _wptLastNextId = reading.NextId;

            if (reading.PassedId.Length == 0) return;

            // ⚠️ This speaks from a SimConnect callback, OUTSIDE the wrap MainForm puts around
            // ProcessSimVarUpdate, so it must check the mute itself — the same rule the baro
            // settle timer and the A32NX armed-altitude flush follow.
            if (Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains(WaypointMonitorKey)) return;

            // Immediate, not queued: a controller expects the report within seconds, and a
            // passing that arrives behind a queue of scan chatter is a passing the pilot
            // reports late. Same reasoning as the settled altimeter value.
            _wptAnnouncer?.AnnounceImmediate(GpsWaypointSequencer.ComposePassing(reading, DistanceText));
        }
        catch (Exception ex)
        {
            Utils.Logging.Log.Debug("DA40", $"Waypoint sequencer: {ex.Message}");
        }
    }

    /// <summary>
    /// Answers Ctrl+W from the LAST DELIVERED frame, never a fresh request — the definition
    /// carries a standing once-a-second subscription and re-issuing its id as a one-shot would
    /// cancel it outright, which is the trap that froze the A380's managed-altitude derivation.
    /// </summary>
    private string ComposeWaypointReadout()
    {
        var last = _wptSimConnect?.LastGpsWaypoint;
        if (last == null) return "Waypoint information not available yet.";
        return GpsWaypointSequencer.ComposeReadout(
            GpsWaypointSequencer.Read(last.Value, _wptLastNextId), DistanceText);
    }

    /// <summary>Answers D — distance and time to the destination, from the standing GPS frame.</summary>
    private string ComposeDestinationReadout()
    {
        var last = _wptSimConnect?.LastGpsWaypoint;
        return last == null
            ? "Destination information not available yet."
            : GpsWaypointSequencer.ComposeDestination(last.Value, DistanceText);
    }

    /// <summary>Answers Shift+D — top of descent, from the G1000's own VNAV.</summary>
    private string ComposeTopOfDescentReadout(SimConnectManager simConnect)
    {
        double avail = simConnect.GetCachedVariableValue("DA40_VNAV_PATH_AVAIL") ?? 0;
        double tod = simConnect.GetCachedVariableValue("DA40_VNAV_TOD_DIST") ?? 0;
        return GpsWaypointSequencer.ComposeTopOfDescent(avail > 0.5, tod, DistanceText);
    }

    /// <summary>Distance in whatever the pilot set on the G1000, never a fixed unit.</summary>
    private string DistanceText(double nm)
    {
        if (DisplayUnitDistance == "metric")
        {
            double km = nm * 1.852;
            return km < 10 ? $"{km:0.0} kilometres" : $"{km:0} kilometres";
        }
        return nm < 10 ? $"{nm:0.0} miles" : $"{nm:0} miles";
    }
}
