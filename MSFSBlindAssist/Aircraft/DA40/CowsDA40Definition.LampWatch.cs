using System;
using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// When a light SWITCH and its LAMP disagree.
///
/// A switch speaks when it moves; the lamp behind it did not, and until now that was the
/// right call — the lamp follows the switch, so announcing both is the same news twice.
///
/// ⚠️ EXCEPT WHEN IT DOES NOT FOLLOW, WHICH IS THE ONLY CASE ANYONE CARES ABOUT. The
/// aeroplane models light failures (the Light Failures panel has twelve) and it models
/// circuit breakers that genuinely gate their circuits — pulling CB_FLP extinguishes
/// FLAP_LIGHT, measured live. So a selected light that is not lit means a failed bulb or a
/// pulled breaker, and it is exactly the thing a sighted pilot notices without trying: they
/// flick the switch and the wing stays dark.
///
/// That is what is announced here, and only that. The lamp agreeing with its switch stays
/// silent, so a normal flight sounds exactly as it did before.
///
/// WHY IT IS DEBOUNCED. A lamp does not light on the same frame as its switch — the model
/// runs its electrical logic on its own tick — so comparing the two the instant either
/// changes reports a disagreement on every single switch press. The pair has to be given
/// time to settle, and only a disagreement that PERSISTS is real.
///
/// WHY IT IS NOT SYMMETRIC. "Selected on, not lit" is a fault. "Lit with the switch off" is
/// also worth saying, because it means something else is powering it, and on an aeroplane
/// whose cabin lights have a shortcut clickspot on the airspeed indicator that is a real
/// way to be surprised.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>How long a disagreement must persist before it is spoken.</summary>
    private const int LampSettleMs = 1500;

    /// <summary>
    /// The switch a lamp is meant to follow, and what to call the pair.
    ///
    /// The cabin lights are deliberately absent: their switch is a three-position selector
    /// rather than an on/off, and the aeroplane also toggles them from a clickspot on the
    /// airspeed indicator, so "disagreement" there has an innocent cause that would cry
    /// wolf. The exterior lights and the ice-inspection light are all plain on/off.
    /// </summary>
    private static readonly Dictionary<string, (string Switch, string What)> LampPairs =
        new(StringComparer.Ordinal)
        {
            ["DA40_LIGHT_LGN_STATE"] = ("DA40_LIGHT_LANDING", "Landing light"),
            ["DA40_LIGHT_TXI_STATE"] = ("DA40_LIGHT_TAXI", "Taxi light"),
            ["DA40_LIGHT_POS_STATE"] = ("DA40_LIGHT_POSITION", "Position lights"),
            ["DA40_LIGHT_STB_STATE"] = ("DA40_LIGHT_STROBE", "Strobe lights")
        };

    /// <summary>Exposed for the tests, which check both halves of every pair exist.</summary>
    public static IReadOnlyDictionary<string, (string Switch, string What)> LampPairKeys => LampPairs;

    private System.Windows.Forms.Timer? _lampTimer;
    private ScreenReaderAnnouncer? _lampAnnouncer;
    private readonly Dictionary<string, bool> _lampSpoken = new(StringComparer.Ordinal);

    /// <summary>
    /// Notes that a lamp or a switch moved and arms the settle. Returns true for a LAMP,
    /// which is how the lamp states stay out of the generic announcer - a lamp agreeing
    /// with its switch is not news and must not speak on its own.
    /// </summary>
    private bool NoteLampChange(string varKey, ScreenReaderAnnouncer announcer)
    {
        bool isLamp = LampPairs.ContainsKey(varKey);
        bool isSwitch = false;
        foreach (var pair in LampPairs)
        {
            if (pair.Value.Switch == varKey) { isSwitch = true; break; }
        }

        if (!isLamp && !isSwitch) return false;

        _lampAnnouncer = announcer;

        if (_lampTimer == null)
        {
            _lampTimer = new System.Windows.Forms.Timer { Interval = LampSettleMs };
            _lampTimer.Tick += (_, _) => FlushLampWatch();
        }

        // Stop-then-start: only the state the pair comes to REST in is judged.
        _lampTimer.Stop();
        _lampTimer.Start();

        // A lamp never reaches the generic announcer; a switch always does, because a
        // switch moving is news in its own right and always was.
        return isLamp;
    }

    private void FlushLampWatch()
    {
        _lampTimer?.Stop();
        if (_lampAnnouncer == null || _simConnectForLamps == null) return;

        var muted = Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet;

        foreach (var pair in LampPairs)
        {
            string lampKey = pair.Key;
            string switchKey = pair.Value.Switch;

            // Muting the SWITCH mutes its lamp too - they are one thing to a pilot, and a
            // row un-ticked in the Monitor Manager should not keep talking through its
            // other half. Checked here because this speaks from a TIMER, outside the wrap
            // that mutes ProcessSimVarUpdate.
            if (muted.Contains(switchKey) || muted.Contains(lampKey)) continue;

            double? lamp = _simConnectForLamps.GetCachedVariableValue(lampKey);
            double? sw = _simConnectForLamps.GetCachedVariableValue(switchKey);
            if (lamp is null || sw is null) continue;

            bool disagrees = (sw.Value >= 0.5) != (lamp.Value >= 0.5);

            // Only the TRANSITION into or out of disagreement, or a standing fault would
            // be re-announced every time any other light was touched.
            _lampSpoken.TryGetValue(lampKey, out bool was);
            if (disagrees == was) continue;
            _lampSpoken[lampKey] = disagrees;

            if (!disagrees)
            {
                _lampAnnouncer.Announce(pair.Value.What + " now agrees with its switch");
                continue;
            }

            _lampAnnouncer.AnnounceImmediate(sw.Value >= 0.5
                ? pair.Value.What + " selected ON but NOT lit. Check the bulb and its breaker."
                : pair.Value.What + " lit with the switch OFF.");
        }
    }

    private SimConnect.SimConnectManager? _simConnectForLamps;

    /// <summary>
    /// Handed the connection so the settle can read both halves of a pair at once. Called
    /// from the same place the other monitors are started.
    /// </summary>
    public void AttachLampWatch(SimConnect.SimConnectManager simConnect)
        => _simConnectForLamps = simConnect;

    private void StopLampWatch()
    {
        try { _lampTimer?.Stop(); _lampTimer?.Dispose(); } catch { }
        _lampTimer = null;
        _lampSpoken.Clear();
    }
}
