using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Announcing a radio frequency that somebody else changed, and saying what a swap
/// ACTUALLY did rather than what it was expected to do.
///
/// FOUR separate faults were reported together here and they had four separate causes.
///
/// 1. A frequency set from OUTSIDE MSFSBA said nothing. SayIntentions tuning COM 1, or the
///    pilot tuning it on the G1000 itself, changed the radio in silence, because the
///    active and standby readouts were <c>OnRequest</c> and <c>IsAnnounced = false</c> -
///    they were never polled and never spoken.
///
/// 2. A second swap in a row did not swap. <c>AnnounceSwap</c> fired the event through
///    <c>ExecuteCalculatorCode</c>, and two consecutive swaps of one radio are two
///    BYTE-IDENTICAL calculator strings, which MobiFlight coalesces - it drops the second.
///    The announcement is composed in C# and so was spoken anyway, which is why it
///    reported a swap that had not happened. This is the same trap as the squawk keypad
///    and the wiper switch; the fix is the same, <c>ExecuteCalculatorCodeUnique</c>.
///
/// 3. The announcement was a PREDICTION, not a read-back. It read the cached standby
///    BEFORE firing and announced that as the new active. So it could only ever be right
///    when the cache was right - and see (4) for why it was not - and it stayed confidently
///    wrong when the event had been dropped by (2).
///
/// 4. The cache it predicted from was never populated. Batch membership requires
///    <c>Continuous AND IsAnnounced</c>, and the standby readouts carried
///    <c>IsAnnounced = false</c>, so <c>GetCachedVariableValue</c> had nothing for them.
///    That is "COM 2 does not remember what the standby is": there was nothing to
///    remember. NAV 1 announcing 113.90 while the radio actually held 110.30 is the same
///    fault - a stale or absent cache read, presented as fact.
///
/// The fix removes the prediction entirely. Every frequency is now Continuous and
/// announced, and the value a pilot hears is the one the RADIO reported after the fact.
/// A swap says only that it swapped; the frequency that follows comes from the aircraft.
///
/// WHY IT IS DEBOUNCED, same reasoning as the altimeter subscales: tuning steps in 25 kHz
/// (COM) or 50 kHz (NAV) increments and a knob sweeps many steps a second. Only the
/// frequency the knob comes to rest on is of any interest.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>How still the tuning must be before the frequency is spoken.</summary>
    private const int RadioSettleMs = 600;

    /// <summary>How long after MSFSBA's own set or swap to stay quiet.</summary>
    private const int RadioOwnWriteGraceMs = 2500;

    /// <summary>
    /// The frequency readouts, and what to call each one. These are IsAnnounced so they
    /// reach the batch cache and this announcer sees them at all; the generic monitor is
    /// kept out of them by <see cref="NoteRadioChange"/> returning true.
    /// </summary>
    private static readonly Dictionary<string, string> RadioLabels = new(StringComparer.Ordinal)
    {
        ["DA40_RADIO_COM1_ACTIVE"] = "COM 1 active",
        ["DA40_RADIO_COM2_ACTIVE"] = "COM 2 active",
        ["DA40_RADIO_NAV1_ACTIVE"] = "NAV 1 active",
        ["DA40_RADIO_NAV2_ACTIVE"] = "NAV 2 active",
        ["DA40_RADIO_COM1_SET"] = "COM 1 standby",
        ["DA40_RADIO_COM2_SET"] = "COM 2 standby",
        ["DA40_RADIO_NAV1_SET"] = "NAV 1 standby",
        ["DA40_RADIO_NAV2_SET"] = "NAV 2 standby",

        // The autopilot's SELECTED values ride the same settle timer, and for the same
        // reason: an altitude preselect knob steps 100 ft at a time and a heading bug one
        // degree, so announcing every step buries the value the pilot stopped on. They had
        // no announcement at all before this - moving a knob on real hardware, or the
        // G1000 changing a preselect, was completely silent.
        ["DA40_AP_ALT_SET"] = "Selected altitude",
        ["DA40_AP_VS_SET"] = "Selected vertical speed",
        ["DA40_AP_IAS_SET"] = "Selected airspeed",
        ["DA40_AP_HDG_SET"] = "Heading bug",
        ["DA40_AP_CRS_SET"] = "Course"
    };

    /// <summary>
    /// Exposed for the suite. These carry IsAnnounced and sit in a panel display list, so
    /// the numeric-silence test would flag them - but they are not on the generic path at
    /// all, they are spoken by the settle timer below.
    /// </summary>
    public static IReadOnlyCollection<string> RadioAnnouncedKeys => RadioLabels.Keys;

    private System.Windows.Forms.Timer? _radioSettleTimer;
    private ScreenReaderAnnouncer? _radioAnnouncer;
    private DateTime _radioOwnWriteAt = DateTime.MinValue;

    private readonly Dictionary<string, double> _radioPending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _radioSpoken = new(StringComparer.Ordinal);

    /// <summary>
    /// Called by every path inside MSFSBA that tunes or swaps, so the settle timer knows
    /// the change was ours. The panel field and the swap button both confirm themselves.
    /// </summary>
    private void MarkRadioSetByUs() => _radioOwnWriteAt = DateTime.UtcNow;

    private bool NoteRadioChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (!RadioLabels.ContainsKey(varKey)) return false;

        _radioAnnouncer = announcer;

        // A first reading is not a change. Without this, the first batch after a connect
        // reads all eight frequencies at a pilot who has touched nothing.
        if (!_radioSpoken.ContainsKey(varKey))
        {
            _radioSpoken[varKey] = value;
            return true;
        }

        // Below half a COM channel there is nothing to say - that is float noise on the
        // wire, not a tuning step.
        if (Math.Abs(value - _radioSpoken[varKey]) < 0.005) return true;

        _radioPending[varKey] = value;

        if (_radioSettleTimer == null)
        {
            _radioSettleTimer = new System.Windows.Forms.Timer { Interval = RadioSettleMs };
            _radioSettleTimer.Tick += (_, _) => FlushRadioSettle();
        }

        // Stop-then-start is what makes it a settle rather than a repeat.
        _radioSettleTimer.Stop();
        _radioSettleTimer.Start();
        return true;
    }

    private void FlushRadioSettle()
    {
        _radioSettleTimer?.Stop();

        var pending = new Dictionary<string, double>(_radioPending);
        _radioPending.Clear();
        if (pending.Count == 0) return;

        bool ours = (DateTime.UtcNow - _radioOwnWriteAt).TotalMilliseconds < RadioOwnWriteGraceMs;
        foreach (var kv in pending) _radioSpoken[kv.Key] = kv.Value;
        if (_radioAnnouncer == null) return;

        // A SWAP is ours but must still be spoken: the whole point of the fix is that the
        // pilot hears what the radio actually did, and a swap moves the ACTIVE frequency.
        // Only a standby the pilot typed is genuinely redundant, because the field already
        // read it back.
        if (ours)
        {
            foreach (var key in RadioLabels.Keys)
                if (key.EndsWith("_SET", StringComparison.Ordinal)) pending.Remove(key);
            if (pending.Count == 0) return;
        }

        // The Ctrl+M mute is checked HERE, not left to the caller. This speaks from a
        // TIMER, and the generic monitor gate only wraps ProcessSimVarUpdate - a callback
        // outside that wrap would keep talking after the pilot un-ticked the row. Same
        // rule the altimeter settle and the A32NX armed-altitude flush follow.
        var muted = Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet;
        foreach (var key in RadioLabels.Keys)
            if (muted.Contains(key)) pending.Remove(key);
        if (pending.Count == 0) return;

        // One utterance when several moved. A swap changes active AND standby together, so
        // two announcements a breath apart is exactly how a pilot loses the first.
        var parts = new List<string>();
        foreach (var kv in RadioLabels)
            if (pending.TryGetValue(kv.Key, out double f))
            {
                // A frequency needs three decimals; an altitude of 9000 read as "9000.000"
                // is a different kind of wrong. Radios are the only three-decimal values
                // here, and they are exactly the keys carrying RADIO in their name.
                bool freq = kv.Key.IndexOf("RADIO", StringComparison.Ordinal) >= 0;
                parts.Add(kv.Value + " " + f.ToString(freq ? "0.000" : "0",
                    System.Globalization.CultureInfo.InvariantCulture));
            }

        if (parts.Count > 0) _radioAnnouncer.Announce(string.Join(". ", parts));
    }

    /// <summary>Stops the settle timer. Called when the aircraft is switched away.</summary>
    private void StopRadioAnnounce()
    {
        try { _radioSettleTimer?.Stop(); _radioSettleTimer?.Dispose(); } catch { }
        _radioSettleTimer = null;
        _radioPending.Clear();
    }
}
