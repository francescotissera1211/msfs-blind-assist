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
            // ⚠️ KEEP THIS IN STEP WITH BezelKeys. It was left advertising F2, F3, F6, F7
            // and F8 after those were removed, and calling Control Enter the cursor knob
            // after it became ENT - so the one place a pilot goes to find out what the
            // keys are was telling them the wrong answer.
            AccessibleDescription = title +
                ". Read with the arrow keys. Enter presses the selected softkey. " +
                // WHAT THE KNOBS DO, in the words of the aircraft's own source rather than
                // the bezel's geometry. Read out of the instrument and then confirmed one
                // keystroke at a time: with the cursor on, the lower knob moves BETWEEN
                // fields and the upper knob acts ON the field you are on - opening its
                // list of choices, or cycling the character of a text box. With the cursor
                // off, either knob opens the page selector instead. Naming the knobs was
                // worse than useless; a pilot who cannot see the bezel needs to know which
                // pair moves and which pair changes.
                "Control with left and right moves between fields once the cursor is on, " +
                "and steps the page group when it is off. Control with up and down CHANGES " +
                "the field you are on - it opens its list of choices, or cycles a letter in " +
                "a text box - and steps the page when the cursor is off. " +
                // Said early and plainly, because it is the fact that makes the setup
                // pages usable: without the cursor the knobs only change pages, which is
                // exactly how the Aux setup page came to read as completely inert.
                "Shift with Enter pushes the cursor, which you need before you can move " +
                "between fields on a setup page. Control with Enter is enter, which " +
                "accepts the choice in an open list. " +
                // The two keys that do not exist on the bezel, and why they are here.
                "Control with G lists every page in the G1000 and opens the one you pick, " +
                "including the ones the knob would take five groups to reach. " +
                "Control with T types straight into the waypoint box under the cursor, " +
                "the same way the display's own keyboard icon does; the aircraft " +
                "autocompletes it and reads back the facility it found. " +
                "Control with D direct to, F flight plan, P procedures, E menu, L clear. " +
                "Control with Page Up and Page Down is the map range. " +
                // Radios are a PFD bezel, so this line is only true there. Said anyway:
                // a pilot who reads it on the MFD learns where the knobs actually live.
                "On the PFD, Alt with the arrows turns the COM knob and control with Alt " +
                "turns the NAV knob; up and down are megahertz, left and right kilohertz, " +
                "and Enter moves the tuning box between radio one and two. " +
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
            try { _rowSettle?.Stop(); _rowSettle?.Dispose(); } catch { }
            _rowSettle = null;
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

    /// <summary>
    /// How long after the last arrow key the list is left alone.
    ///
    /// ⚠️ THIS IS WHY THE READER KEPT SAYING "NOT SELECTED". The window re-reads the
    /// display about once a second, and rows legitimately appear and disappear as it does -
    /// the field list arrives when the cursor is armed, a box heading arrives with it, a
    /// caution clears. <see cref="DisplayList.UpdateInPlace"/> then has to move the
    /// selection to follow the row the pilot was on, and a ListBox whose selection is
    /// re-set under a screen reader re-announces the item WITH its state.
    ///
    /// So while the pilot is actually moving through the list, the list holds still. The
    /// scrape keeps running - the CAS watcher and the units reader are fed from it and are
    /// never delayed - only the redraw waits, and only until they pause.
    /// </summary>
    private const int RowQuietMs = 2500;

    private DateTime _lastNavKeyAt = DateTime.MinValue;
    private List<string>? _heldRows;
    private System.Windows.Forms.Timer? _rowSettle;

    /// <summary>
    /// The keys that move the reader through the list.
    ///
    /// ⚠️ EVERY HANDLED KEY DEFERS THE REDRAW TOO, not just these - see ProcessCmdKey. A
    /// bezel key forces an immediate re-scrape, and rewriting the list under a screen
    /// reader is what made it read out engine rows nobody asked for ("Oil Temp: green 65
    /// percent along") in the middle of a page change. The bezel key SPEAKS its own answer,
    /// so the list can wait until the pilot stops pressing things.
    /// </summary>
    private static bool IsReadingKey(Keys keyData) => keyData switch
    {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right or
        Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown => true,
        _ => false
    };

    private void OnRowsUpdated(List<string> rows)
    {
        if (_disposed || !IsHandleCreated) return;
        _gotRows = true;

        // ANNOUNCEMENTS FIRST, AND NEVER HELD. This window holds one of the two inspector
        // sockets, so the always-on CAS watcher cannot have it; feeding the watcher here is
        // what keeps cautions speaking while the window is open. Delaying that to tidy up
        // the list would trade a caution for a redraw.
        if (_side == "PFD") _owner?.ProcessCasRows(rows);
        else _owner?.NoteDisplayUnits(rows);

        // Hold the redraw while the pilot is reading. The rows themselves are NOT held
        // back from anything else - the caller has already fed the CAS watcher and the
        // units reader by the time this runs.
        if ((DateTime.UtcNow - _lastNavKeyAt).TotalMilliseconds < RowQuietMs)
        {
            _heldRows = rows;

            if (_rowSettle == null)
            {
                _rowSettle = new System.Windows.Forms.Timer { Interval = 400 };
                _rowSettle.Tick += (_, _) =>
                {
                    if (_disposed) { _rowSettle?.Stop(); return; }
                    if ((DateTime.UtcNow - _lastNavKeyAt).TotalMilliseconds < RowQuietMs) return;

                    _rowSettle!.Stop();
                    var held = _heldRows;
                    _heldRows = null;
                    if (held != null) ApplyRows(held);
                };
            }

            _rowSettle.Start();
            return;
        }

        ApplyRows(rows);
    }

    private void ApplyRows(List<string> rows)
    {

        // The client raises this on the thread that created it, which is this UI thread —
        // but a handle can be destroyed between the check and the call on a close race, so
        // the marshal is still guarded.
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                _text.SetLines(rows.Count > 0 ? rows : new List<string> { "No data from the display." });
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
        // ENTER means ENTER. Ctrl+Enter used to be the CURSOR push, which is the one key
        // on the bezel a pilot reaches for expecting "confirm" - so the obvious key did
        // the unobvious thing and the confirm key was F3, three rows away.
        [Keys.Control | Keys.Enter] = ("ENT_Push", "enter"),
        [Keys.Shift | Keys.Enter]   = ("FMS_Upper_PUSH", "cursor"),

        // The named bezel buttons take their own initial under Ctrl, so the whole bezel
        // sits on ONE modifier rather than two.
        [Keys.Control | Keys.D]     = ("DIRECTTO", "direct to"),
        [Keys.Control | Keys.F]     = ("FPL_Push", "flight plan"),
        [Keys.Control | Keys.P]     = ("PROC_Push", "procedures"),
        [Keys.Control | Keys.E]     = ("MENU_Push", "menu"),
        [Keys.Control | Keys.L]     = ("CLR", "clear"),
        // ⚠️ THE WAY OUT OF A STUCK POPUP, WHICH DID NOT EXIST. A view opened over a page -
        // Waypoint Information, a duplicate-ident picker - swallows the bezel buttons beneath
        // it, so PROC and FPL silently do nothing while it is up, and CLR does NOT close one
        // (measured: firing AS1000_MFD_CLR at a stuck WptInfo left it exactly where it was).
        // A pilot who typed an ident that resolved somewhere unexpected had no way back.
        // Not plain Escape - that closes this window, which a pilot mid-entry does not want.
        [Keys.Control | Keys.Shift | Keys.L] = ("__ESCAPE__", "closed"),

        // NO function-key aliases. Two ways to press one button is two things to learn
        // and two things to document, and the F-keys were never mnemonic. F5 survives
        // elsewhere in this form, but that is the house-wide REFRESH key every MSFSBA
        // status display uses - it is not a bezel button.
        // THE RADIO KNOBS. PFD ONLY - the G1000 tunes its radios on the PFD and the MFD has
        // no COM or NAV knob at all, which is the answer to "can I even tune them from the
        // MFD": no, and neither can a sighted pilot. Nothing in MSFSBA could drive them
        // before this, so the Radios panel was the only way to tune, and the bezel a
        // sighted pilot uses was simply unavailable.
        //
        // Verified live rather than guessed, one event at a time against the real radio:
        // COM small stepped standby 127.200 to 127.205 (8.33 kHz spacing, as the setup
        // page has it), COM large stepped 127.205 to 128.205, and the NAV knob moved NAV 2
        // - not NAV 1 - because NAV 2 held the tuning box, exactly as the PFD reported.
        //
        // Alt is COM, Control+Alt is NAV. Up and Down are the LARGE knob (MHz), Left and
        // Right the SMALL one (kHz), and Enter is the knob PUSH, which moves the tuning
        // box between radio 1 and 2 - it does NOT swap. Swapping stays on the Radios
        // panel's own buttons, which drive the stock events and are already proven.
        // ⚠️ ONLY THE PUSH. The four TURN events of each radio knob are NOT here - see
        // RadioKnobKeys below, and the measurement that put them there.
        [Keys.Alt | Keys.Enter]                = ("COM_Push", "COM tuning box"),
        [Keys.Control | Keys.Alt | Keys.Enter] = ("NAV_Push", "NAV tuning box"),

        [Keys.Control | Keys.PageUp]   = ("RANGE_INC", "range out"),
        [Keys.Control | Keys.PageDown] = ("RANGE_DEC", "range in")
    };

    /// <summary>
    /// THE RADIO TUNING KNOBS, WHICH GO BACK OVER SIMCONNECT.
    ///
    /// ⚠️ This is the second measured exception on this display and it is the OPPOSITE of
    /// the first. The FMS knob and the named bezel buttons reach the instrument ONLY
    /// through its own <c>onInteractionEvent</c>; the radio knobs reach the radio ONLY
    /// through a SimConnect H-event write, and firing them down the socket does nothing at
    /// all. Measured both ways on COM 2's standby, minutes apart:
    ///
    ///   onInteractionEvent("AS1000_PFD_COM_Small_INC")  →  124.850, unchanged
    ///   1 (&gt;H:AS1000_PFD_COM_Small_INC) over SimConnect →  124.855
    ///
    /// The likeliest reason is that these are consumed by the aeroplane's own gauge logic
    /// rather than by the JavaScript instrument, but the reason does not matter — the
    /// measurement does. What DOES matter is that moving the whole bezel onto the socket
    /// silently killed radio tuning from this window, which is exactly what "how do I tune
    /// the radios?" meant.
    ///
    /// The knob PUSH is the exception to the exception: it moves the tuning box between
    /// radio 1 and 2, it works over the socket, and it reads back there, so it stays in
    /// <see cref="BezelKeys"/>.
    ///
    /// NOTHING IS SPOKEN HERE. Every frequency is Continuous and announced, and the settle
    /// announcer speaks the value the knob comes to rest on — announcing the keystroke as
    /// well would put a word in front of the only thing the pilot is waiting for. That is
    /// the same reasoning that removed the swap's prediction.
    /// </summary>
    private static readonly Dictionary<Keys, (string Event, string Spoken)> RadioKnobKeys = new()
    {
        [Keys.Alt | Keys.Up]                   = ("COM_Large_INC", "COM megahertz up"),
        [Keys.Alt | Keys.Down]                 = ("COM_Large_DEC", "COM megahertz down"),
        [Keys.Alt | Keys.Right]                = ("COM_Small_INC", "COM kilohertz up"),
        [Keys.Alt | Keys.Left]                 = ("COM_Small_DEC", "COM kilohertz down"),

        [Keys.Control | Keys.Alt | Keys.Up]    = ("NAV_Large_INC", "NAV megahertz up"),
        [Keys.Control | Keys.Alt | Keys.Down]  = ("NAV_Large_DEC", "NAV megahertz down"),
        [Keys.Control | Keys.Alt | Keys.Right] = ("NAV_Small_INC", "NAV kilohertz up"),
        [Keys.Control | Keys.Alt | Keys.Left]  = ("NAV_Small_DEC", "NAV kilohertz down"),

        // ⚠️ THE SWAP, WITHOUT WHICH THE WHOLE RADIO FEATURE IS DECORATIVE. The knob PUSH
        // (Alt+Enter, Ctrl+Alt+Enter) moves the tuning cursor between COM 1 and COM 2, and
        // between NAV 1 and NAV 2 - press it again and it comes back - but nothing here could
        // ever make a tuned STANDBY frequency ACTIVE. So a pilot could dial 118.7 perfectly,
        // hear it read back as set, and still be transmitting on the old frequency.
        //
        // Reported from the cockpit as "I don't know how to make it go back to NAV 1 ... and I
        // don't know how to swap it either". The first half was the push doing exactly its job;
        // the second half was a feature that had never existed.
        //
        // Shift is added to the push that already selects the radio, so the family stays
        // learnable: Alt is COM, Control+Alt is NAV, Enter pushes the knob, Shift+Enter swaps.
        [Keys.Alt | Keys.Shift | Keys.Enter]                = ("COM_Switch", "COM swapped"),
        [Keys.Control | Keys.Alt | Keys.Shift | Keys.Enter] = ("NAV_Switch", "NAV swapped"),

        // THE BAROMETRIC KNOB, which is on the same bezel and takes the same transport.
        // Ctrl+B still sets both altimeters to a number you type, and that stays the fast
        // way to answer a controller's QNH - but the knob is how the aeroplane is flown,
        // and a pilot nudging the setting a hectopascal at a time down an approach should
        // not have to open a dialog to do it. Verified live: one INC moved the G1000 from
        // 1011.85 to 1012.19 hPa, which is the aeroplane's own 0.01 inHg step.
        [Keys.Control | Keys.Shift | Keys.Up]   = ("BARO_INC", "barometric setting up"),
        [Keys.Control | Keys.Shift | Keys.Down] = ("BARO_DEC", "barometric setting down")
    };

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Note that the pilot is WORKING the display, so the poll does not rewrite the list
        // under them. Reading keys are not handled here - the list still gets them.
        if (IsReadingKey(keyData) || BezelKeys.ContainsKey(keyData) ||
            RadioKnobKeys.ContainsKey(keyData) || keyData == Keys.Enter)
        {
            _lastNavKeyAt = DateTime.UtcNow;
        }

        if (keyData == Keys.F5)
        {
            // F5 means "give me it NOW", so it also cancels the read-quiet hold - otherwise
            // a refresh pressed straight after an arrow key would sit in the queue for two
            // and a half seconds and look like a dead key.
            _lastNavKeyAt = DateTime.MinValue;
            _ = _client.ScrapeNowAsync();
            return true;
        }

        if (keyData == Keys.Enter)
        {
            return TryPressSelectedSoftkey();
        }

        // GO TO A PAGE. The knob route to a page is five groups by nine pages behind a
        // selector that closes itself, counted by ear; this is the same viewService.open
        // the selector makes, chosen off a list.
        if (keyData == (Keys.Control | Keys.G))
        {
            _ = ShowPageJumpAsync();
            return true;
        }

        // TYPE INTO THE FIELD UNDER THE CURSOR. The G1000's own text boxes take keyboard
        // entry - that is what the little keyboard icon beside them is - and this drives
        // the same input component, so the database search and the autocomplete run
        // exactly as they do for a sighted pilot.
        if (keyData == (Keys.Control | Keys.T))
        {
            ShowTypeDialog();
            return true;
        }

        // The radio knobs, over SimConnect. UNIQUE because a knob is turned in bursts and
        // two identical calc strings in a row are what MobiFlight coalesces - which on a
        // radio means every second click of a sweep goes missing.
        if (RadioKnobKeys.TryGetValue(keyData, out var knob))
        {
            if (_side != "PFD")
            {
                // Said rather than swallowed: the G1000 tunes on the PFD and the MFD has
                // no COM or NAV knob at all, which is a real answer to "why does this key
                // do nothing here" and one a sighted pilot can see at a glance. The
                // barometric knob is on the same bezel and the same is true of it.
                _announcer.AnnounceImmediate(knob.Event.StartsWith("BARO", StringComparison.Ordinal)
                    ? "The barometric knob is on the PFD, not the MFD."
                    : "The radios are tuned on the PFD, not the MFD.");
                return true;
            }

            if (!_simConnect.IsConnected)
            {
                _announcer.AnnounceImmediate("Not connected to simulator.");
                return true;
            }

            // ⚠️ THE BARO KNOB SHARES THIS TABLE AND IS NOT A RADIO. Routing it through the
            // radio read-back would poll six times for a change that can never appear in
            // A.radios(), and say nothing. It has its own settle announcer on the variable,
            // which is the right channel for it - this only needs the scrape so the window's
            // own rows refresh.
            if (knob.Event.StartsWith("BARO", StringComparison.Ordinal))
            {
                _simConnect.ExecuteCalculatorCodeUnique($"1 (>H:AS1000_PFD_{knob.Event})");
                _ = _client.ScrapeNowAsync();
                return true;
            }

            _ = TurnRadioKnob(knob.Event);
            return true;
        }

        if (BezelKeys.TryGetValue(keyData, out var bezel))
        {
            // Not a bezel button at all — it closes whatever view is sitting OVER the page.
            // Routed through the same table so it inherits the read-quiet hold and the
            // read-back, and so there is one place to look for "what does this key do".
            if (bezel.Event == "__ESCAPE__") _ = CloseStuckView();
            else _ = PressBezel(bezel.Event, bezel.Spoken);
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
    /// <summary>
    /// Shut any view stacked over the page, and say where that left the pilot.
    ///
    /// ⚠️ This is not something a bezel button can do. A view opened over a page swallows the
    /// buttons underneath it, so PROC and FPL go silently dead while it is up, and CLR does
    /// NOT dismiss one — measured live against a stuck Waypoint Information window. Without
    /// this key a pilot whose typed ident resolved somewhere unexpected had no way back at all.
    /// </summary>
    private async Task CloseStuckView()
    {
        try
        {
            string r = await _client.InvokeAsync(
                "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.M.escape()");
            // Naming the page is the useful half: "closed" alone leaves a pilot wondering
            // what they closed and where they now are.
            string page = await _client.InvokeAsync(
                "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.M.pageKey()");
            _announcer.AnnounceImmediate(
                r == "nothing open" ? "Nothing to close." : $"Closed. {page}");
        }
        catch (Exception ex)
        {
            Utils.Logging.Log.Debug("DA40", $"Close view: {ex.Message}");
            _announcer.AnnounceImmediate("Could not close the window.");
        }
    }

    /// <summary>
    /// Turn a radio knob and read the frequency it landed on.
    ///
    /// ⚠️ THE DISPLAY NEEDS A FRAME, AND THIS PATH NEVER WAITED FOR ONE. It fired the event
    /// and scraped in the same breath, so the read came from BEFORE the keystroke: the row
    /// looked unchanged, nothing was announced, and the pilot's NEXT press read back the
    /// PREVIOUS one's frequency. Reported as "it was at 710, I pressed twice, I got 715" -
    /// 715 was the first press, and the radio was already on 725 by the time it was spoken.
    ///
    /// PressBezel has carried the identical fix, and its own comment names this symptom
    /// ("turning a knob kept repeating where it was because the answer came from before the
    /// keystroke"); the radio knobs simply never got it. A read showing nothing NEW is
    /// retried rather than believed.
    ///
    /// ⚠️ It reads over the COHERENT SOCKET, not the 1 Hz batch, which is what makes a prompt
    /// answer possible at all - the settle announcer is for changes made ELSEWHERE and must
    /// stay long enough to outlast a batch period. A knob the pilot just turned is a
    /// deliberate action with an expected answer, and waiting a second and a half for it is
    /// what made the tuning feel unreliable.
    /// </summary>
    private async Task TurnRadioKnob(string knobEvent)
    {
        const string Expr = "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.radios().join(\" | \")";

        string before;
        try { before = await _client.InvokeAsync(Expr); } catch { before = ""; }
        if (_disposed) return;

        _simConnect.ExecuteCalculatorCodeUnique($"1 (>H:AS1000_PFD_{knobEvent})");

        // Bounded retry. A frame is normally enough; a few give the slow case room without
        // ever leaving the key feeling unanswered.
        string after = before;
        for (int i = 0; i < 6 && !_disposed; i++)
        {
            await Task.Delay(90);
            if (_disposed) return;
            try { after = await _client.InvokeAsync(Expr); } catch { break; }
            if (after.Length > 0 && after != before) break;
        }
        if (_disposed) return;

        _ = _client.ScrapeNowAsync();

        // Speak only the FIELD that moved. Nothing changed at all stays silent rather than
        // repeating the old value, which is the specific lie this method exists to stop.
        var moved = FirstChangedRadioField(before, after);

        // ⚠️ A KNOB PRESS THAT CHANGED NOTHING SAYS NOTHING, DELIBERATELY. It is tempting to
        // explain the silence - and a first attempt did, announcing "Avionics master off" off
        // a SimVar reading. That diagnosis was WRONG (COM AVAILABLE was 1 and the PFD was
        // drawing CAS messages and softkeys the whole time, so the G1000 was plainly powered),
        // and the pilot's ruling is that a state the DISPLAY can report should be scanned for,
        // not announced at them. The real cause is on the row itself now: NAV 2 and COM 2 carry
        // FAILED, so a scan says why the knob does nothing while a press stays quiet.
        if (!moved.Found) return;

        // ⚠️ TELL THE SETTLE ANNOUNCER THIS WAS OURS, or the pilot hears it twice: the
        // read-back here, and the same frequency again a second later when the 1 Hz batch
        // delivers it. Measured from the cockpit as
        //   "COM 1, active 127.850, standby 121.725"   (this window)
        //   "COM 1 standby 121.725"                    (the settle, 1.2 s later)
        // The settle exists for a change made ELSEWHERE; a knob turned in this window is not
        // one, and this is the only place that knows which key moved.
        if (moved.VarKey.Length > 0) _owner?.MarkRadioTunedByWindow(moved.VarKey);

        _announcer.AnnounceImmediate(moved.Spoken);
    }

    /// <summary>
    /// Which frequency moved, in the words a pilot tuning it wants, and the variable it
    /// belongs to.
    ///
    /// ⚠️ ONLY THE FIELD THAT CHANGED. Reading the whole row back - "COM 1, active 127.850,
    /// standby 121.735" - recites the frequency the pilot did NOT touch on every single step
    /// of the knob, and the one they did is buried at the end of it. Tuning is a stream of
    /// presses; the answer has to be short enough to survive being heard twenty times.
    /// </summary>
    internal readonly struct RadioFieldChange
    {
        public string Spoken { get; init; }
        /// <summary>The MSFSBA key, so the settle announcer can be told this was ours.</summary>
        public string VarKey { get; init; }
        public bool Found => Spoken.Length > 0;
    }

    /// <summary>
    /// The one FIELD that differs between two radio scrapes. Pure so the suite can pin it.
    /// </summary>
    internal static RadioFieldChange FirstChangedRadioField(string before, string after)
    {
        var none = new RadioFieldChange { Spoken = "", VarKey = "" };
        if (after.Length == 0 || after == before) return none;

        var b = before.Split('|');
        var a = after.Split('|');

        for (int i = 0; i < a.Length; i++)
        {
            string rowAfter = a[i].Trim();
            string rowBefore = i < b.Length ? b[i].Trim() : "";
            if (rowAfter.Length == 0 || rowAfter == rowBefore) continue;

            // "COM 1, active 127.850, standby 121.735, TUNING, TRANSMIT"
            var fa = rowAfter.Split(',');
            var fb = rowBefore.Split(',');
            if (fa.Length == 0) continue;

            string radio = fa[0].Trim();               // "COM 1"

            for (int f = 1; f < fa.Length; f++)
            {
                string valAfter = fa[f].Trim();
                string valBefore = f < fb.Length ? fb[f].Trim() : "";
                if (valAfter == valBefore) continue;

                // TUNING and TRANSMIT move when the knob PUSH shifts the cursor between
                // radios, which is a real change and worth saying - but it is not a
                // frequency, so it is named as itself rather than dressed as one.
                if (valAfter == "TUNING" || valAfter == "TRANSMIT" ||
                    valBefore == "TUNING" || valBefore == "TRANSMIT")
                    continue;

                return new RadioFieldChange
                {
                    Spoken = radio + " " + valAfter,   // "COM 1 standby 121.735"
                    VarKey = RadioVarKey(radio, valAfter)
                };
            }
        }
        return none;
    }

    /// <summary>
    /// "COM 1" + "standby 121.735" -> DA40_RADIO_COM1_SET. Empty when it cannot be mapped,
    /// which only costs the duplicate suppression and never the announcement.
    /// </summary>
    private static string RadioVarKey(string radio, string field)
    {
        string kind = radio.StartsWith("COM", StringComparison.Ordinal) ? "COM"
                    : radio.StartsWith("NAV", StringComparison.Ordinal) ? "NAV" : "";
        if (kind.Length == 0) return "";

        string n = radio.EndsWith("2", StringComparison.Ordinal) ? "2" : "1";
        string suffix = field.StartsWith("standby", StringComparison.Ordinal) ? "SET"
                      : field.StartsWith("active", StringComparison.Ordinal) ? "ACTIVE" : "";
        if (suffix.Length == 0) return "";

        return $"DA40_RADIO_{kind}{n}_{suffix}";
    }

    private async Task PressBezel(string eventSuffix, string spoken)
    {
        string name = $"AS1000_{_side}_{eventSuffix}";

        // ONE round trip: fire the key and get the read-back together. This was four -
        // summary before, key, wait, summary again, scrape - and each is a full socket
        // exchange with the Coherent debugger.
        var (cursorOn, view, focus, summary, accepted) = await FireAndRead(name);
        if (_disposed || !accepted) return;

        // THE DISPLAY NEEDS A FRAME, and reading before it has had one is what made this
        // window feel haunted: pressing the cursor said nothing until the SECOND press,
        // and turning a knob "kept repeating where it was" because the answer came from
        // before the keystroke. So a read that shows nothing NEW is retried rather than
        // believed. What counts as new is the cursor flag OR the text - the flag matters
        // on its own, because arming the cursor can leave the summary identical for a
        // moment and that is precisely the case being missed.
        if (cursorOn == _lastCursorOn && summary == _lastSpokenSummary)
        {
            // ONLY THE PAGE SELECTOR IS SLOW, and now the read-back says which view it is
            // looking at, so only the page selector pays for it. Before the view key
            // existed this was one wait for every key, which is why four arrow presses in
            // a row felt like the window had hung.
            bool selector = view == "PageSelect" || _lastView == "PageSelect";
            await Task.Delay(selector ? PageSelectSettleMs : BezelSettleMs);
            if (_disposed) return;

            (cursorOn, view, focus, summary, accepted) = await FireAndRead(null);
            if (_disposed || !accepted) return;
        }

        string toSay = summary.Length > 0 ? summary : spoken;

        // THE CURSOR IS ANNOUNCED WHEN IT CHANGES, WITHIN ONE VIEW, AND AT NO OTHER TIME.
        //
        // It used to prefix "Cursor on." to EVERY field, so a pilot walking fourteen
        // fields down a setup page heard it fourteen times for one bit of information they
        // already had. But the two transitions are both real news and neither marks
        // itself: turning it off leaves only the page title, which is indistinguishable
        // from a key that did nothing - so a pilot presses the cursor again to check,
        // turns it back on, and cannot tell the two states apart.
        //
        // ⚠️ THE VIEW TEST IS WHAT STOPS IT LYING. Every view owns its OWN scroll
        // controller, so the flag being read changes the moment a different view is on top
        // - and the page SELECTOR is a view, opened by the very knob the pilot is turning.
        // Reported from the cockpit as the cursor switching itself on and off: turning the
        // knob opened the selector (its flag), the selector closed onto a page (the page's
        // flag), and each swap read as a cursor the pilot had never touched. A cursor
        // change only means anything when it happens to the SAME view twice running.
        if (view == _lastView && cursorOn != _lastCursorOn)
        {
            toSay = (cursorOn ? "Cursor on. " : "Cursor off. ") + toSay;
        }

        // THE KNOB DOES NOT WRAP. At the end of a page the G1000 simply stops, and the
        // window then read the same field back on every further turn with nothing to say
        // why - reported from the cockpit as "Minimum Length" fifteen times running. If
        // the cursor is on, the focused field did not move and the key was a knob turn,
        // say which end of the page it is. Same view only, for the same reason as above.
        else if (view == _lastView && cursorOn && focus.Length > 0 && focus == _lastFocus &&
                 KnobDirection(eventSuffix) is int direction)
        {
            toSay += direction > 0 ? ", end of the page" : ", start of the page";
        }

        _lastCursorOn = cursorOn;
        _lastSpokenSummary = summary;
        _lastView = view;
        _lastFocus = focus;
        _announcer.AnnounceImmediate(toSay);

        // The window's own text last, off the critical path: it is not what a pilot is
        // waiting on after a keystroke.
        await _client.ScrapeNowAsync();
    }

    /// <summary>
    /// Fires a bezel key (or just re-reads, when <paramref name="name"/> is null) and
    /// unpacks the agent's "ok|cursor|summary" answer.
    /// </summary>
    private async Task<(bool CursorOn, string View, string Focus, string Summary, bool Accepted)> FireAndRead(string? name)
    {
        string call = name is null
            ? "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.state()"
            : $"window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.press('{name}')";

        string result = await _client.InvokeAsync(call);
        if (_disposed) return (false, "", "", "", false);

        // A.key answers "no instrument" when the view has no G1000 element to drive, which
        // is the one failure a pilot could otherwise mistake for a dead key.
        if (result.IndexOf("no instrument", StringComparison.Ordinal) >= 0)
        {
            _announcer.AnnounceImmediate("The display did not accept that key.");
            return (false, "", "", "", false);
        }

        // "ok|cursor|view|focus|summary".
        //
        // The VIEW key decides how long to wait: only the page SELECTOR holds its choice
        // for about a second before committing, and paying that wait on every key is what
        // made the window feel slow enough to be broken.
        //
        // The FOCUS index is which field the cursor is on. The G1000's knob does NOT wrap
        // at the end of a page, so without it a pilot at the bottom of a setup page heard
        // the same field read back a dozen times with nothing to say it was the last one.
        var parts = result.Split('|');
        if (parts.Length < 5) return (false, "", "", "", true);

        return (parts[1] == "1", parts[2].Trim(), parts[3].Trim(),
            string.Join("|", parts, 4, parts.Length - 4).Trim(), true);
    }

    /// <summary>What was last read back, so a repeat can be told from a stale read.</summary>
    private string _lastSpokenSummary = "";

    /// <summary>Whether the cursor was on at the last read-back.</summary>
    private bool _lastCursorOn;

    /// <summary>Which view the display was showing at the last read-back.</summary>
    private string _lastView = "";

    /// <summary>Which field the cursor was on at the last read-back, as the agent numbers them.</summary>
    private string _lastFocus = "";

    /// <summary>
    /// Whether a bezel key is a FIELD-MOVING knob turn, and which way. Only those can run
    /// off the end of a page; a value key that leaves the focus where it was has not hit
    /// any kind of end and must not be told it has.
    /// </summary>
    private static int? KnobDirection(string eventSuffix) => eventSuffix switch
    {
        "FMS_Lower_INC" => 1,
        "FMS_Lower_DEC" => -1,
        _ => null
    };

    /// <summary>
    /// How long to wait before reading back a bezel press.
    ///
    /// The 1200 ms is real but it is NOT general: it exists because the page SELECTOR the
    /// FMS knob opens holds its choice for about a second before committing, so a shorter
    /// wait reads the old page and reports the knob as having done nothing. Applying it to
    /// every key made the whole window feel broken - a list highlight moves on the frame
    /// it is asked to, and waiting 1.2 s to say so, four keys in a row, is an eternity.
    ///
    /// So the wait is now per-key. Only the two knob events that can open the page
    /// selector pay it; everything else - ENT, CLR, MENU, the direct keys - reads back
    /// almost at once.
    /// </summary>
    private const int BezelSettleMs = 500;

    /// <summary>
    /// How long the PAGE SELECTOR takes to commit. It holds the page the knob landed on for
    /// about a second before opening it, so a read-back sooner than this reports the page
    /// the pilot has just left and the knob reads as having done nothing.
    /// </summary>
    private const int PageSelectSettleMs = 1200;



    /// <summary>
    /// Lists every page the G1000 has, and opens the one the pilot picks.
    ///
    /// The list comes from the display's OWN page table — the same table the page selector
    /// draws — so it cannot drift out of step with the aeroplane, and it carries the keys
    /// that say which of those names have a page behind them. Seven of the nine Aux pages
    /// do not; the list says so rather than hiding them, because a blind pilot otherwise
    /// has no way to tell a page the G1000 never built from one this window cannot read.
    /// </summary>
    private async Task ShowPageJumpAsync()
    {
        string raw = await _client.InvokeAsync(
            "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.pageList()");
        if (_disposed) return;

        var entries = new List<CowsDA40PageJumpForm.PageEntry>();
        foreach (string line in raw.Split('\n'))
        {
            var bits = line.Trim().Split('|');
            if (bits.Length < 2 || bits[0].Length == 0) continue;
            entries.Add(new CowsDA40PageJumpForm.PageEntry(
                bits[0].Trim(), bits[1].Trim(), bits.Length > 2 ? bits[2].Trim() : ""));
        }

        if (entries.Count == 0)
        {
            _announcer.AnnounceImmediate("The display did not give a page list.");
            return;
        }

        // Which page we are on, so the list opens where the pilot already is.
        var (_, current, _, _, _) = await FireAndRead(null);
        if (_disposed) return;

        using var picker = new CowsDA40PageJumpForm(entries, _announcer, current);
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (picker.SelectedKey is not { Length: > 0 } key) return;
        if (_disposed) return;

        string result = await _client.InvokeAsync(
            "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.goPage('" + key + "')");
        if (_disposed) return;

        var parts = result.Split('|');
        if (parts.Length >= 4)
        {
            _lastCursorOn = parts[1] == "1";
            _lastView = parts[2].Trim();
            _lastSpokenSummary = string.Join("|", parts, 3, parts.Length - 3).Trim();
            _announcer.AnnounceImmediate(_lastSpokenSummary);
        }
        else
        {
            // "not available" comes back for a page whose key the display does not know,
            // which should be impossible from this list but is worth saying rather than
            // leaving the key silent.
            _announcer.AnnounceImmediate(result.Length > 0 ? result : "The page did not open.");
        }

        await _client.ScrapeNowAsync();
    }

    /// <summary>
    /// Types an ident into whatever text field the cursor is on.
    ///
    /// THIS IS THE DISPLAY'S OWN KEYBOARD ENTRY, not a way round the knobs. The G1000's
    /// waypoint boxes take typed input — that is what the small keyboard icon beside them
    /// is for — and this drives the same input component the icon does, so the aircraft's
    /// database search, its autocomplete and its facility lookup all run exactly as they do
    /// for a sighted pilot. Nothing here reimplements any of them.
    ///
    /// The knob route is untouched and still works character by character: Control with up
    /// and down cycles the letter under the edit cursor, Control with left and right moves
    /// along the box. But spelling a four-letter ident that way is up to twenty-eight knob
    /// clicks, and a pilot flying an approach does not have twenty-eight knob clicks.
    ///
    /// The read-back is DELAYED on purpose. The aeroplane debounces its own database
    /// search, so asking straight away returns the previous waypoint's name — which is
    /// exactly how "VCRI" first read back as Bandaranaike.
    /// </summary>
    private void ShowTypeDialog()
    {
        var dialog = new ValueInputForm(
            "Type into the G1000", "ident", "Letters and digits, for example VCRI", _announcer,
            input =>
            {
                string clean = Sanitise(input);
                if (clean.Length == 0) return (false, "Enter letters or digits, for example VCRI");
                if (clean.Length > 6) return (false, "Six characters at most");
                return (true, "");
            });

        dialog.ShowCancelButton = true;
        if (dialog.ShowDialog(this) != DialogResult.OK || !dialog.IsValidInput) return;

        _ = TypeIntoFieldAsync(Sanitise(dialog.InputValue));
    }

    /// <summary>Only what the G1000's own character set holds: A to Z and 0 to 9.</summary>
    private static string Sanitise(string input)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in (input ?? "").ToUpperInvariant())
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
        return sb.ToString();
    }

    private async Task TypeIntoFieldAsync(string ident)
    {
        if (ident.Length == 0) return;

        string result = await _client.InvokeAsync(
            "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.typeIdent('" + ident + "')");
        if (_disposed) return;

        if (result.IndexOf("ok", StringComparison.Ordinal) != 0)
        {
            // "no text field" is the common one, and it is worth spelling out: the cursor
            // has to be ON a box before there is anything to type into.
            _announcer.AnnounceImmediate(
                result.IndexOf("no text field", StringComparison.Ordinal) >= 0
                    ? "Nothing to type into. Put the cursor on a waypoint field first."
                    : "The display did not take that: " + result);
            return;
        }

        await Task.Delay(TypeSearchMs);
        if (_disposed) return;

        string said = await _client.InvokeAsync(
            "window.__MSFSBA_DA40G1000 && window.__MSFSBA_DA40G1000.typedResult()");
        if (_disposed) return;

        _announcer.AnnounceImmediate(said);
        await _client.ScrapeNowAsync();
    }

    /// <summary>
    /// How long the aeroplane's own waypoint search takes to settle.
    ///
    /// Measured rather than chosen: typing VCR and reading back at once returned the
    /// PREVIOUS search's match, and the correct one - autocompleted to VCRAD - arrived a
    /// few hundred milliseconds later. The wait is only paid when something was typed.
    /// </summary>
    private const int TypeSearchMs = 900;

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
