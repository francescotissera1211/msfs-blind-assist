using System.Text.RegularExpressions;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.DA40;

/// <summary>
/// Live read-out window for a COWS DA40 G1000 display.
///
/// The scrape is deliberately NARROW — CAS, FMA, navigation source, softkeys — because
/// those are the things only the display knows. Airspeed, altitude, vertical speed,
/// heading and the barometric setting are stock SimVars the readout hotkeys already answer
/// from, and the DOM is a far worse source for them: the airspeed box is a SCROLLER whose
/// textContent is the entire digit strip rather than the value.
///
/// It owns its own socket for one view and disposes it on close, so it never contends with
/// another client. Modelled on <see cref="HS787.HS787DisplayForm"/>, which does the same
/// job for the 787's displays; the only reason this is not that class is the agent file.
///
/// READ OVER COHERENT, WRITE OVER SIMCONNECT. Pressing a softkey does not go back down the
/// debugger socket: `1 (>H:AS1000_PFD_SOFTKEYS_11)` through the ordinary calculator path
/// presses it, verified live by watching the row change. That keeps the socket read-only,
/// which is one less thing that can wedge it, and it is the same split the A380 RMP uses.
///
/// ONE SOFTKEY IS ONE BUTTON. A press either cycles a value in place or replaces all twelve
/// keys with a sub-menu whose own row carries a Back key — so after every press the window
/// re-reads immediately and speaks the new row, because the keys under the pilot's fingers
/// have just changed.
///
/// THE REST OF THE BEZEL GOES DOWN THE SOCKET, and that split is measured rather than
/// chosen: the softkeys answer over SimConnect, the FMS knob, MENU, ENT, CLR, Direct-To,
/// FPL, PROC, the range knob and the map joystick do not — not plainly, and not with the
/// `901 0 *` uniquifying prefix that defeats MobiFlight's duplicate-command dedup either.
/// Fired through the instrument's own onInteractionEvent they all work. So `A.key(...)`
/// over the debugger is the only road for those, exactly as the A380's KCCU keys reach
/// its MFD only on the display's own event bus.
///
/// THE KEY MAP AVOIDS EVERYTHING THE LIST ITSELF USES. The window is read with the arrow
/// keys, Home, End, Page Up and Page Down, and first-letter type-ahead — so none of those
/// may be taken, or reading the display would cost the pilot the ability to move around
/// it. The bezel therefore sits on Ctrl+arrows and the function keys.
/// </summary>
public sealed class CowsDA40DisplayForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Matches the softkey rows the agent emits. The prefix is a CONTRACT between this
    /// form and coherent-da40-g1000-agent.js: change the wording in one and this stops
    /// finding them, so both sides say so.
    /// </summary>
    private static readonly Regex SoftkeyRow = new(@"^Softkey (\d{1,2}):", RegexOptions.Compiled);

    private readonly CoherentDisplayClient _client;
    private readonly DisplayListBox _text;
    private readonly IntPtr _previousWindow;
    private readonly SimConnectManager _simConnect;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _side;
    private readonly System.Windows.Forms.Timer _connectWatchdog;
    private readonly Aircraft.DA40.CowsDA40Definition? _owner;
    private bool _gotRows;
    private bool _disposed;

    public CowsDA40DisplayForm(string title, string coherentViewNeedle, string side,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Aircraft.DA40.CowsDA40Definition? owner = null)
    {
        _owner = owner;
        _simConnect = simConnect;
        _announcer = announcer;
        _side = side;

        _previousWindow = GetForegroundWindow();

        Text = title;
        Size = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _text = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            TabIndex = 0,
            AccessibleName = title,
            AccessibleDescription = title +
                ". Read with the arrow keys. Enter presses the selected softkey. " +
                "Control with left and right steps the page group, control with up and " +
                "down steps the page. F2 menu, F3 enter, F4 clear, F6 direct to, " +
                "F7 flight plan, F8 procedures, control with Enter is the cursor knob, " +
                "control with Page Up and Page Down is the map range. " +
                "F5 refreshes; Escape closes. Auto-updates."
        };
        _text.SetText("Connecting to the display...");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(560, 8),
            Size = new Size(90, 30),
            TabIndex = 1,
            AccessibleName = "Refresh"
        };
        refreshButton.Click += (s, e) => _ = _client?.ScrapeNowAsync();

        var closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(655, 8),
            Size = new Size(85, 30),
            TabIndex = 2,
            DialogResult = DialogResult.OK,
            AccessibleName = "Close"
        };
        closeButton.Click += (s, e) => Close();

        bottom.Controls.AddRange(new Control[] { refreshButton, closeButton });
        Controls.Add(_text);
        Controls.Add(bottom);
        CancelButton = closeButton;

        _client = new CoherentDisplayClient(coherentViewNeedle, pollIntervalMs: 1200,
            agentFileName: "coherent-da40-g1000-agent.js");
        _client.RowsUpdated += OnRowsUpdated;

        // A CLIENT ERROR MUST REACH THE PILOT. Without this the window sits on
        // "Connecting to the display..." for ever with no reason given, which is
        // indistinguishable from a hung app - and every cause below is one the pilot can
        // actually do something about.
        _client.Error += OnClientError;

        // And if nothing arrives at all, say so rather than leaving the first line
        // standing. Coherent GT allows only ONE inspector socket per view, so the usual
        // reason is that something else already holds this display.
        _connectWatchdog = new System.Windows.Forms.Timer { Interval = 6000 };
        _connectWatchdog.Tick += (s, e) =>
        {
            _connectWatchdog!.Stop();
            if (_disposed || _gotRows) return;
            _text.SetLines(new List<string>
            {
                "Could not read the display.",
                "",
                "The G1000 allows only one debugger connection per screen, so the usual",
                "cause is that something else is already reading this one - another copy",
                "of this window, or a developer tool.",
                "",
                "Check the sim is running with the aircraft loaded, then press F5."
            });
        };

        Load += (s, e) =>
        {
            BringToFront();
            Activate();
            _text.Focus();
            // ONE INSPECTOR SOCKET PER VIEW. The background CAS watcher holds AS1000_PFD,
            // so it has to let go before this window can read the same screen - and the
            // window wins, because a pilot who deliberately opened the display is already
            // reading it.
            if (_side == "PFD") _owner?.SuspendCasMonitor(true);
            _client.Start();
            _connectWatchdog.Start();
        };

        FormClosed += (s, e) =>
        {
            _connectWatchdog.Stop();
            _connectWatchdog.Dispose();
            _client.RowsUpdated -= OnRowsUpdated;
            _client.Error -= OnClientError;
            _client.Stop();
            _client.Dispose();
            if (_side == "PFD") _owner?.SuspendCasMonitor(false);
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    private void OnClientError(string message)
    {
        if (_disposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                _text.SetLines(new List<string> { "Display error: " + message, "", "Press F5 to retry." });
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void OnRowsUpdated(List<string> rows)
    {
        if (_disposed || !IsHandleCreated) return;
        _gotRows = true;

        // The client raises this on the thread that created it, which is this UI thread —
        // but a handle can be destroyed between the check and the call on a close race, so
        // the marshal is still guarded.
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                _text.SetLines(rows.Count > 0 ? rows : new List<string> { "No data from the display." });

                // This window holds the PFD socket, so the always-on CAS watcher cannot.
                // Feed it what we just read instead of leaving it blind - a caution
                // arriving while the pilot is reading some other part of the display is
                // exactly the one that has to speak.
                if (_side == "PFD") _owner?.ProcessCasRows(rows);
            }));
        }
        catch (InvalidOperationException)
        {
            // Handle destroyed while closing; nothing to update.
        }
    }

    /// <summary>
    /// The bezel, keyed. Every entry is an H-event this build of the G1000 actually
    /// declares — the map was read out of the instrument's own event table rather than
    /// guessed, so a key here cannot be one the display silently discards. The joystick
    /// pan is MFD-only, which is why it is not in this shared table.
    /// </summary>
    private static readonly Dictionary<Keys, (string Event, string Spoken)> BezelKeys = new()
    {
        [Keys.Control | Keys.Right] = ("FMS_Lower_INC", "next page group"),
        [Keys.Control | Keys.Left]  = ("FMS_Lower_DEC", "previous page group"),
        [Keys.Control | Keys.Down]  = ("FMS_Upper_INC", "next page"),
        [Keys.Control | Keys.Up]    = ("FMS_Upper_DEC", "previous page"),
        [Keys.Control | Keys.Enter] = ("FMS_Upper_PUSH", "cursor"),
        [Keys.F2]                   = ("MENU_Push", "menu"),
        [Keys.F3]                   = ("ENT_Push", "enter"),
        [Keys.F4]                   = ("CLR", "clear"),
        [Keys.F6]                   = ("DIRECTTO", "direct to"),
        [Keys.F7]                   = ("FPL_Push", "flight plan"),
        [Keys.F8]                   = ("PROC_Push", "procedures"),
        [Keys.Control | Keys.PageUp]   = ("RANGE_INC", "range out"),
        [Keys.Control | Keys.PageDown] = ("RANGE_DEC", "range in")
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            _ = _client.ScrapeNowAsync();
            return true;
        }

        if (keyData == Keys.Enter)
        {
            return TryPressSelectedSoftkey();
        }

        if (BezelKeys.TryGetValue(keyData, out var bezel))
        {
            _ = PressBezel(bezel.Event, bezel.Spoken);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Fires one bezel key in the page and reads the result back.
    ///
    /// The settle is longer than a softkey's because the FMS knob opens a SELECTOR that
    /// commits about a second after the last turn — read too early and the window reports
    /// the page the pilot has just left. What it speaks is the page TITLE rather than a
    /// confirmation of the keystroke: "Aux - System Setup 1" is the answer to what the
    /// knob did, and "next page group" is only what was asked for.
    /// </summary>
    private async Task PressBezel(string eventSuffix, string spoken)
    {
        string name = $"AS1000_{_side}_{eventSuffix}";
        string result = await _client.InvokeAsync(
            $"window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.key('{name}')");
        if (_disposed) return;

        // A.key answers "no instrument" when the view has no G1000 element to drive, which
        // is the one failure a pilot could otherwise mistake for a dead key.
        if (result.IndexOf("no instrument", StringComparison.Ordinal) >= 0)
        {
            _announcer.AnnounceImmediate("The display did not accept that key.");
            return;
        }

        await Task.Delay(BezelSettleMs);
        if (_disposed) return;

        // Refresh the window's own text first, then ask the display what to SAY. The two
        // are different questions: the list wants everything on screen, the pilot who just
        // turned a knob wants the one line that answers what the knob did.
        await _client.ScrapeNowAsync();
        if (_disposed) return;

        // InvokeAsync returns the value already unwrapped - CoherentDisplayClient's
        // ExtractValue calls GetString() on a string result - so there is no JSON quoting
        // to strip here.
        string summary = (await _client.InvokeAsync(
            "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.summary()")).Trim();
        _announcer.AnnounceImmediate(summary.Length > 0 ? summary : spoken);
    }

    /// <summary>
    /// How long to wait before reading back a bezel press. The page selector the FMS knob
    /// opens holds its choice for about a second before committing, so a shorter wait
    /// reads the OLD page and reports the knob as having done nothing.
    /// </summary>
    private const int BezelSettleMs = 1200;

    /// <summary>
    /// Presses the softkey on the selected row, if it is one. Returns false for any other
    /// row so Enter keeps its normal meaning there rather than being swallowed.
    /// </summary>
    private bool TryPressSelectedSoftkey()
    {
        if (_text.SelectedIndex < 0 || _text.SelectedItem is null) return false;

        var match = SoftkeyRow.Match(_text.SelectedItem.ToString() ?? "");
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, out int key) || key < 1 || key > 12) return false;

        if (!_simConnect.IsConnected)
        {
            _announcer.AnnounceImmediate("Not connected to simulator.");
            return true;
        }

        // What the display said BEFORE the press, so the read-back can say what the press
        // actually did rather than only whether the menu changed.
        var before = new List<string>(_client.CurrentRows);
        string pressedLabel = LabelOf(_text.SelectedItem?.ToString() ?? "");

        // UNIQUE, not plain. MobiFlight's command channel coalesces two consecutive
        // byte-identical calc strings, and pressing the SAME softkey twice in a row is
        // exactly that - so the second press was silently dropped. That is not an edge
        // case on this display: typing a squawk of 1000 presses the "0" key three times
        // running, and stepping the CDI past a source means pressing CDI again. Reported
        // from the cockpit as a squawk entry that took the first digit and then nothing.
        // The codebase's own invariant already says every valueless calc write goes
        // through the unique form; this call was the exception that proved it.
        _simConnect.ExecuteCalculatorCodeUnique($"1 (>H:AS1000_{_side}_SOFTKEYS_{key})");

        // A press can replace the whole row, so read it back at once rather than waiting
        // for the next poll - the pilot needs to know what the keys became.
        _ = ReadBackAfterPress(before, pressedLabel);
        return true;
    }

    /// <summary>
    /// Says what a softkey press DID.
    ///
    /// A press does one of two things, and only one of them used to be spoken. Replacing
    /// the twelve keys with a sub-menu was announced; CYCLING A VALUE IN PLACE was silent,
    /// on the reasoning that re-reading twelve unchanged labels would be noise. The
    /// reasoning was right and the conclusion was wrong: CDI and OBS both cycle in place,
    /// so pressing them produced no sound at all and both read as dead keys — reported as
    /// exactly that, "the CDI page is not clickable or doesn't appear to be". They work
    /// perfectly: CDI steps LOC1, LOC2, GPS, and OBS toggles suspend, measured live.
    ///
    /// So when the labels do not change, the FIRST LINE THAT DID is spoken instead. That
    /// is the navigation source for CDI and OBS, the units row for ALT Units, the
    /// barometric row for STD Baro — in every case the thing the key was pressed to
    /// change, and never twelve labels the pilot already knows.
    /// </summary>
    private async Task ReadBackAfterPress(List<string> before, string pressedLabel)
    {
        // One short settle: the display rebuilds its softkey row over a frame or two.
        await Task.Delay(250);
        if (_disposed) return;

        var rows = await _client.ScrapeNowAsync();
        if (_disposed) return;

        string joined = string.Join(", ", SoftkeyLabels(rows));
        if (joined != string.Join(", ", SoftkeyLabels(before)))
        {
            _announcer.AnnounceImmediate("Softkeys now: " + joined);
            return;
        }

        // Softkey rows are excluded on purpose: the labels are identical by the branch
        // above, and a key's own VALUE field rides on its row, so including them would
        // announce "Softkey 6: CDI GPS" where the pilot wants "Navigation source: GPS".
        string changed = FirstChangedRow(before, rows);
        if (changed.Length > 0)
        {
            _announcer.AnnounceImmediate(changed);
            return;
        }

        // A PRESS IS NEVER SILENT. If nothing on the display moved, the key itself is
        // spoken back — otherwise the pilot cannot tell a key that did nothing from one
        // that was not registered at all, which is the complaint that started this
        // ("you never know when they're entered"). It costs one short word and removes a
        // whole class of doubt.
        if (pressedLabel.Length > 0) _announcer.AnnounceImmediate(pressedLabel);
    }

    /// <summary>
    /// The label off a softkey row, e.g. "Softkey 3: Standby" gives "Standby". Blank rows
    /// read as "blank", which is a real answer and is left alone.
    /// </summary>
    private static string LabelOf(string row)
    {
        var m = SoftkeyRow.Match(row);
        if (!m.Success) return "";
        string rest = row.Substring(row.IndexOf(':') + 1).Trim();
        return rest;
    }

    private static List<string> SoftkeyLabels(IEnumerable<string> rows) =>
        rows.Where(r => SoftkeyRow.IsMatch(r))
            .Select(r => r.Substring(r.IndexOf(':') + 1).Trim())
            .ToList();

    /// <summary>
    /// The first non-softkey row that differs.
    ///
    /// Compared BY LABEL rather than by position, because a press can add or remove a row
    /// — a window opening, a caution clearing — and a positional compare would then call
    /// every row after it changed.
    ///
    /// ⚠️ A LABEL IS NOT UNIQUE. The CAS block emits one row per message and they all read
    /// "Caution: ...", so a plain label map kept only the first and then matched the
    /// SECOND caution against it, found them different, and announced a standing caution
    /// after every single keypress. Reported from the cockpit as exactly that: "whenever I
    /// press enter on something, the first caution on the list gets announced". So a row's
    /// identity is its label AND which occurrence of that label it is.
    ///
    /// A row that only EXISTS in the new state is not a change either — it is a window
    /// that just opened, and the softkey branch above has already spoken for that.
    /// </summary>
    private static string FirstChangedRow(List<string> before, List<string> after)
    {
        var old = BuildRowMap(before);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string row in after)
        {
            if (SoftkeyRow.IsMatch(row)) continue;
            string label = RowLabel(row);
            if (label.Length == 0) continue;

            seen.TryGetValue(label, out int n);
            seen[label] = n + 1;

            string key = label + "#" + n;
            if (old.TryGetValue(key, out string? was) && was != row.Trim()) return row.Trim();
        }

        return "";
    }

    private static Dictionary<string, string> BuildRowMap(IEnumerable<string> rows)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string row in rows)
        {
            if (SoftkeyRow.IsMatch(row)) continue;
            string label = RowLabel(row);
            if (label.Length == 0) continue;

            counts.TryGetValue(label, out int n);
            counts[label] = n + 1;
            map[label + "#" + n] = row.Trim();
        }

        return map;
    }

    /// <summary>The part before the colon, which is the row's stable identity.</summary>
    private static string RowLabel(string row)
    {
        int colon = row.IndexOf(':');
        return colon <= 0 ? "" : row.Substring(0, colon).Trim();
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
