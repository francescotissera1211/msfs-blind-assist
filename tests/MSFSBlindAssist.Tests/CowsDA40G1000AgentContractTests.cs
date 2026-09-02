using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The contract between the DA40's G1000 window and the agent it injects.
///
/// These two files talk to each other by NAME across a WebSocket, so nothing in the
/// compiler notices when one side is renamed. Every fault guarded here has already
/// happened once:
///
///   - the window parsed the agent's answer by field COUNT, and the agent grew a field;
///   - the agent defined A.press TWICE, so the first one was silently unreachable;
///   - the window called a function the agent no longer exposed, and the key went dead
///     with no error anywhere.
///
/// So the shape of the answer, the names the window calls, and the absence of duplicate
/// definitions are all asserted here rather than found in the cockpit.
/// </summary>
public class CowsDA40G1000AgentContractTests
{
    private static string Agent()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources",
            "coherent-da40-g1000-agent.js");
        Assert.True(File.Exists(path), "Agent not copied to the output: " + path);
        return File.ReadAllText(path);
    }

    private static string Form()
    {
        // Walk up out of bin/<config>/<tfm> to the repository, then to the form. The form
        // is the OTHER half of the contract and there is no other way to compare them.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string path = Path.Combine(dir!.FullName, "MSFSBlindAssist", "Forms", "DA40",
            "CowsDA40DisplayForm.cs");
        Assert.True(File.Exists(path), "Display form not found: " + path);
        return File.ReadAllText(path);
    }

    [Fact]
    public void EveryAgentFunctionTheWindowCallsExists()
    {
        string agent = Agent();
        string form = Form();

        // Every __MSFSBA_DA40G1000.<name>( the form invokes must be defined in the agent.
        var called = Regex.Matches(form, @"__MSFSBA_DA40G1000\.(\w+)\(")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(called);

        foreach (string name in called)
        {
            Assert.True(agent.Contains("A." + name + " = function", StringComparison.Ordinal),
                "The window calls " + name + "() but the agent does not define it.");
        }
    }

    [Fact]
    public void TheWindowsScrapeEntryPointExists()
    {
        Assert.Contains("window.__MSFSBA_DISP", Agent(), StringComparison.Ordinal);
        Assert.Contains("MSFSBA_DISP_INSTALLED", Agent(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoFunctionIsDefinedTwiceOnTheAgent()
    {
        // A.press was defined twice - once for softkeys, once for the bezel - and the
        // second silently replaced the first, so a whole branch of the agent had never run.
        var names = Regex.Matches(Agent(), @"^\s*A\.(\w+) = function", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();

        var duplicates = names.GroupBy(n => n)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Defined more than once on the agent, so only the last one exists: " +
            string.Join(", ", duplicates));
    }

    [Fact]
    public void TheStateAnswerCarriesFourFields()
    {
        // "ok|cursor|view|summary". The window splits on '|' and reads index 2 as the view
        // and 3 onwards as the summary, so the agent growing or losing a field silently
        // shifts every one of them.
        Assert.Contains("\"ok|\" + cursor + \"|\" + key + \"|\" + summary", Agent(),
            StringComparison.Ordinal);
        Assert.Contains("if (parts.Length < 4)", Form(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSoftkeyRowPrefixMatchesWhatTheWindowLooksFor()
    {
        // The window finds pressable rows with a regex on "Softkey N:", which is the other
        // by-name contract between these two files.
        Assert.Contains("\"Softkey \" + key.index", Agent(), StringComparison.Ordinal);
        Assert.Contains("^Softkey (\\d{1,2}):", Form(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheAgentAsksTheInstrumentForTheCursorRatherThanTheStylesheet()
    {
        string agent = Agent();

        // The cursor had been read off CSS classes and was wrong on every page whose class
        // this reader had not met. It is the instrument's own scroll controller now.
        Assert.Contains("getIsScrollEnabled", agent, StringComparison.Ordinal);
        Assert.Contains("A.M.cursor = function", agent, StringComparison.Ordinal);

        // And the DOM reader survives ONLY as the fallback for a display whose instrument
        // element is not up yet - which is what the null answer means.
        Assert.Contains("if (modelCursor === null)", agent, StringComparison.Ordinal);
    }

    [Fact]
    public void BothControlFrameworksAreRead()
    {
        string agent = Agent();

        // The flight plan page and the checklist keep their controls in G1000UiControl, not
        // in the scroll controller, and reading only the latter reported both as empty.
        Assert.Contains("_UICONTROL_", agent, StringComparison.Ordinal);
        Assert.Contains("getFocusedIndex", agent, StringComparison.Ordinal);
        Assert.Contains("A.M.f2Say = function", agent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void AircraftOptionsPanelHoldsOnlyWhatTheG1000MenuDoesNot(DA40Variant variant)
    {
        // The MFD Engine page menu carries Failures Mode, State Saving, Engine Damage,
        // Realistic Parking Brake, Panel Shake, Steering Mode, Trim Speed and Prop Speed,
        // and the display window now reads and drives that menu. Those eight are therefore
        // duplicates and were removed; these two are not on the menu and have nowhere else
        // to live.
        var controls = new CowsDA40Definition(variant).GetPanelControls()["Aircraft Options"];

        Assert.Equal(new[] { "DA40_OPT_TIMER_EXPIRED", "DA40_OPT_KILL_FMA" }, controls.ToArray());
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheOptionsRemovedFromThePanelAreStillWatched(DA40Variant variant)
    {
        // Removing the CONTROLS must not remove the ability to HEAR a setting change on the
        // MFD. Every one of the eight is still a defined, announced variable.
        var vars = new CowsDA40Definition(variant).GetVariables();

        foreach (string key in new[]
        {
            "DA40_OPT_STATE_SAVING", "DA40_OPT_DAMAGE", "DA40_OPT_FAILURES_MODE",
            "DA40_OPT_REALISTIC_PARK_BRAKE", "DA40_OPT_WHEEL_ASSIST",
            "DA40_OPT_TRIM_SPEED", "DA40_OPT_SLOW_PROPS", "DA40_OPT_PANEL_SHAKE"
        })
        {
            Assert.True(vars.ContainsKey(key), key + " is no longer defined at all.");
            Assert.True(vars[key].IsAnnounced, key + " would change in silence.");
        }
    }
}
