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
    private bool _disposed;

    public CowsDA40DisplayForm(string title, string coherentViewNeedle, string side,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
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
                ". Read with the arrow keys. Press Enter on a softkey to press it. " +
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

        Load += (s, e) =>
        {
            BringToFront();
            Activate();
            _text.Focus();
            _client.Start();
        };

        FormClosed += (s, e) =>
        {
            _client.RowsUpdated -= OnRowsUpdated;
            _client.Stop();
            _client.Dispose();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    private void OnRowsUpdated(List<string> rows)
    {
        if (_disposed || !IsHandleCreated) return;

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

        return base.ProcessCmdKey(ref msg, keyData);
    }

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

        _simConnect.ExecuteCalculatorCode($"1 (>H:AS1000_{_side}_SOFTKEYS_{key})");

        // A press can replace the whole row, so read it back at once rather than waiting
        // for the next poll - the pilot needs to know what the keys became.
        _ = ReadBackAfterPress(key);
        return true;
    }

    private async Task ReadBackAfterPress(int key)
    {
        // One short settle: the display rebuilds its softkey row over a frame or two.
        await Task.Delay(250);
        if (_disposed) return;

        var rows = await _client.ScrapeNowAsync();
        if (_disposed) return;

        var labels = rows.Where(r => SoftkeyRow.IsMatch(r))
                         .Select(r => r.Substring(r.IndexOf(':') + 1).Trim())
                         .ToList();

        // Only speak when the row actually CHANGED into a different menu. A key that
        // cycles a value in place leaves the labels alone, and re-reading twelve unchanged
        // keys over the pilot would be noise.
        string joined = string.Join(", ", labels);
        if (joined != _lastSoftkeyLine)
        {
            _lastSoftkeyLine = joined;
            _announcer.AnnounceImmediate("Softkeys now: " + joined);
        }
    }

    private string _lastSoftkeyLine = "";

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
