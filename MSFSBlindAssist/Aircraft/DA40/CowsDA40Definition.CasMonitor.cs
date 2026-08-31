using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The always-on CAS and FMA watcher.
///
/// The PFD window is where a pilot READS the display; this is what tells them to look. It
/// runs whether or not that window is open, because a caution appearing is not something
/// you should have to be already watching for — and on this aeroplane the CAS window is
/// the ONLY place most failures are announced at all. There are four physical annunciator
/// lamps; everything else the AFM calls an annunciation is drawn on the G1000.
///
/// BASELINE-FIRST, like every other MSFSBA monitor. The first scrape after connecting is
/// absorbed silently: reconnecting mid-flight must not re-read every message that was
/// already standing, which is exactly what the A380 EWD monitor had to learn.
///
/// It shares no socket with the display window. Coherent GT allows only ONE inspector
/// connection per view, so this client and the window's client would fight over
/// AS1000_PFD — the monitor therefore SUSPENDS itself while the window is open and
/// resumes when it closes, and the window is the one that wins, because a pilot who has
/// deliberately opened the display is already reading it.
/// </summary>
public partial class CowsDA40Definition
{
    private CoherentDisplayClient? _casClient;
    private ScreenReaderAnnouncer? _casAnnouncer;

    private readonly HashSet<string> _knownCas = new(StringComparer.Ordinal);
    private string _lastFma = "";
    private bool _casBaselined;

    /// <summary>Set by the PFD window while it holds the socket.</summary>
    private bool _casSuspended;

    public void StartCasMonitor(ScreenReaderAnnouncer announcer)
    {
        if (_casClient != null) return;

        _casAnnouncer = announcer;
        _casBaselined = false;
        _knownCas.Clear();
        _lastFma = "";

        _casClient = new CoherentDisplayClient("AS1000_PFD", pollIntervalMs: 1500,
            agentFileName: "coherent-da40-g1000-agent.js");
        _casClient.RowsUpdated += OnCasRows;
        _casClient.Error += msg => Log.Debug("DA40", $"CAS monitor: {msg}");
        _casClient.Start();
    }

    public void StopCasMonitor()
    {
        if (_casClient == null) return;

        _casClient.RowsUpdated -= OnCasRows;
        try { _casClient.Stop(); _casClient.Dispose(); } catch { /* teardown must not throw */ }
        _casClient = null;
        _casAnnouncer = null;
    }

    /// <summary>
    /// The PFD window calls this while it is open. One inspector socket per view means the
    /// two clients cannot both hold AS1000_PFD, and the window wins: a pilot reading the
    /// display does not also need it announced at them.
    /// </summary>
    public void SuspendCasMonitor(bool suspended)
    {
        _casSuspended = suspended;
        _casClient?.SetActive(!suspended);

        // Coming back from suspension, re-baseline: the window may have acknowledged
        // messages or the aeroplane moved on while this was not watching, and announcing
        // the difference as though it just happened would be a lie about when.
        if (!suspended) _casBaselined = false;
    }

    private void OnCasRows(List<string> rows)
    {
        if (_casSuspended || _casAnnouncer == null) return;

        var cas = new List<string>();
        string fma = "";

        foreach (string row in rows)
        {
            if (row.StartsWith("  ", StringComparison.Ordinal)) cas.Add(row.Trim());
            else if (row.StartsWith("Autopilot: ", StringComparison.Ordinal)) fma = row.Substring(11);
        }

        // First pass after a connect is the baseline and is never spoken.
        if (!_casBaselined)
        {
            _casBaselined = true;
            _knownCas.Clear();
            foreach (string c in cas) _knownCas.Add(c);
            _lastFma = fma;
            return;
        }

        foreach (string message in cas)
        {
            if (_knownCas.Add(message)) _casAnnouncer.AnnounceImmediate(message);
        }

        // A message that CLEARED is worth one word, not silence: a caution going away is
        // how a pilot learns the thing they just did worked.
        _knownCas.RemoveWhere(known =>
        {
            if (cas.Contains(known)) return false;
            _casAnnouncer.Announce(known.Replace("Caution: ", "").Replace("WARNING: ", "")
                                        .Replace("Advisory: ", "").Replace("Status: ", "") + " cleared");
            return true;
        });

        if (fma != _lastFma)
        {
            _lastFma = fma;
            // "off" is the resting state and announcing it on every disconnect would be
            // noise; the modes themselves are the news.
            if (fma != "off") _casAnnouncer.AnnounceImmediate("Autopilot " + fma);
        }
    }
}
