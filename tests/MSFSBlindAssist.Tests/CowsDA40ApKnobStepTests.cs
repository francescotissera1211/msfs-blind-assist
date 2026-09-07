using System.IO;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ A TYPED BOX IS A CONVENIENCE, NOT THE CONTROL.
///
/// All five GFC 700 selected values - altitude, vertical speed, airspeed, heading bug and
/// course - could only be TYPED, which is a thing the cockpit does not have. A sighted pilot
/// turns a knob and watches the number step. The pilot asked the general form of it: "are we
/// able to do the same thing as the sighted pilots do, the same way they do?" For these five
/// the answer was no, and the typed box hid the gap precisely because it worked.
///
/// Both paths stay. The Radios panel already keeps its typed entry beside a working bezel for
/// the same reason - typing is faster when you know the number, stepping is what you do when
/// you are searching for it or matching a clearance still being read to you.
///
/// ⚠️ EVERY EVENT NAME HERE WAS MEASURED LIVE, not taken from a list. The DA40's autopilot has
/// no airframe variables at all, so these are stock events and a wrong stock name is a silent
/// no-op - the exact failure mode that shipped a dead Start Key and a dead TO/GA before it.
/// </summary>
public class CowsDA40ApKnobStepTests
{
    private static string AutopilotSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
            dir = dir.Parent;
        Assert.True(dir != null, "Could not locate the repository root.");
        return File.ReadAllText(Path.Combine(dir!.FullName, "MSFSBlindAssist", "Aircraft",
            "DA40", "CowsDA40Definition.Autopilot.cs"));
    }

    [Theory]
    // key, the stock event measured live, the VALUE key whose settle announcer speaks it
    [InlineData("DA40_AP_ALT_UP", "AP_ALT_VAR_INC", "DA40_AP_ALT_SET")]
    [InlineData("DA40_AP_ALT_DN", "AP_ALT_VAR_DEC", "DA40_AP_ALT_SET")]
    [InlineData("DA40_AP_VS_UP", "AP_VS_VAR_INC", "DA40_AP_VS_SET")]
    [InlineData("DA40_AP_VS_DN", "AP_VS_VAR_DEC", "DA40_AP_VS_SET")]
    [InlineData("DA40_AP_IAS_UP", "AP_SPD_VAR_INC", "DA40_AP_IAS_SET")]
    [InlineData("DA40_AP_IAS_DN", "AP_SPD_VAR_DEC", "DA40_AP_IAS_SET")]
    [InlineData("DA40_AP_HDG_UP", "HEADING_BUG_INC", "DA40_AP_HDG_SET")]
    [InlineData("DA40_AP_HDG_DN", "HEADING_BUG_DEC", "DA40_AP_HDG_SET")]
    [InlineData("DA40_AP_CRS_UP", "VOR1_OBI_INC", "DA40_AP_CRS_SET")]
    [InlineData("DA40_AP_CRS_DN", "VOR1_OBI_DEC", "DA40_AP_CRS_SET")]
    public void EachStepFiresTheEventThatWasMeasuredAndMarksItsOwnValue(
        string key, string stockEvent, string valueKey)
    {
        // ⚠️ THE VALUE KEY IS THE HALF THAT IS EASY TO GET WRONG. The settle announcer
        // watches the VALUE, so marking the button's own key would suppress nothing and the
        // pilot would hear every detent twice - once from the step and once when the batch
        // delivered the same change.
        string src = AutopilotSource();
        var m = Regex.Match(src, Regex.Escape($"case \"{key}\":") + @"[^\n]*");
        Assert.True(m.Success, $"{key} has no case in HandleUIVariableSet - the button is dead");
        Assert.Contains($"\"{stockEvent}\"", m.Value);
        Assert.Contains($"\"{valueKey}\"", m.Value);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryStepIsAButtonAndNoneEarnsAMonitorRow(DA40Variant variant)
    {
        // A detent is an ACTION, not a state: nothing to read back, and pressing it again
        // does not undo it - so it carries no resting label. And a control that announces
        // nothing must not earn a Ctrl+M checkbox that mutes nothing.
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        foreach (string key in new[]
        {
            "DA40_AP_ALT_UP", "DA40_AP_ALT_DN", "DA40_AP_VS_UP", "DA40_AP_VS_DN",
            "DA40_AP_IAS_UP", "DA40_AP_IAS_DN", "DA40_AP_HDG_UP", "DA40_AP_HDG_DN",
            "DA40_AP_CRS_UP", "DA40_AP_CRS_DN"
        })
        {
            Assert.True(vars.ContainsKey(key), $"{key} is not defined");
            var d = vars[key];
            Assert.True(d.RenderAsButton, $"{key} must be a button");
            Assert.True(d.SuppressRestingButtonState, $"{key} must carry no resting label");
            Assert.True(d.ExcludeFromMonitorManager, $"{key} must not earn a mute row");
        }
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryStepSitsOnTheGfc700PanelBesideTheValueItMoves(DA40Variant variant)
    {
        // A control nobody can reach is the same as no control. The pair reads directly
        // after its typed value so a pilot arrowing the panel meets the three together.
        // ⚠️ BOTH VARIANTS. The GFC 700 and the G1000 are the SAME fit on the NG and the
        // XLS - the Lycoming changes the engine, not the avionics - so a variant-gated
        // autopilot panel would be a bug, and this is what says so out loud.
        var def = new CowsDA40Definition(variant);
        var panels = def.GetPanelControls();

        Assert.True(panels.ContainsKey("GFC 700"));
        var rows = panels["GFC 700"];

        foreach (string value in new[] { "ALT", "VS", "IAS", "HDG", "CRS" })
        {
            int at = rows.IndexOf($"DA40_AP_{value}_SET");
            Assert.True(at >= 0, $"DA40_AP_{value}_SET missing from the panel");
            Assert.Equal($"DA40_AP_{value}_UP", rows[at + 1]);
            Assert.Equal($"DA40_AP_{value}_DN", rows[at + 2]);
        }
    }

    [Fact]
    public void AStepNeverAnnouncesForItselfBecauseAKnobIsTurnedInBursts()
    {
        // ⚠️ The opposite of what the typed setters do, deliberately. A typed set is ONE
        // value the pilot already knows, so saying it back confirms it landed; a detent is
        // one of a BURST, and announcing each would read a sweep from 5,000 to 9,000 feet
        // as forty separate numbers - the recital the radio read-back exists to avoid.
        string src = AutopilotSource();
        var body = Regex.Match(src, @"private bool StepApValue\(.*?\n    \}", RegexOptions.Singleline);
        Assert.True(body.Success, "StepApValue not found");
        // ⚠️ Match the CALL, not the word - the method takes a ScreenReaderAnnouncer, whose
        // type name contains "Announce" and made the first version of this test fail on its
        // own parameter list.
        Assert.DoesNotContain("announcer.Announce", body.Value);
    }
}
