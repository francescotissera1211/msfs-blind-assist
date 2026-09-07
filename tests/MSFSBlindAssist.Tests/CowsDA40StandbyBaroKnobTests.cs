using System;
using System.IO;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE AEROPLANE HAS THIS KNOB AND MSFSBA COULD ONLY TYPE AT IT.
///
/// The standby altimeter subscale was a typed box alone, which is not a thing the cockpit has:
/// a sighted pilot grabs INSTRUMENT_Knob_Altimeter_1 and turns it. Same gap as the five GFC 700
/// selected values, found by the same question - are we able to do what a sighted pilot does,
/// the way they do it?
///
/// ⚠️ THE STEP IS THE MODEL'S OWN RPN, COPIED VERBATIM from COWS_DA40NG_IN.xml rather than
/// written afresh - including its clamps, which are the aeroplane's and not ours. Verified live
/// both ways: 29.92 -> 29.93 -> 29.92, with the STATE_BARO2 mirror following each step.
/// </summary>
public class CowsDA40StandbyBaroKnobTests
{
    private static string StandbySource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
            dir = dir.Parent;
        Assert.True(dir != null, "Could not locate the repository root.");
        return File.ReadAllText(Path.Combine(dir!.FullName, "MSFSBlindAssist", "Aircraft",
            "DA40", "CowsDA40Definition.Standby.cs"));
    }

    [Theory]
    [InlineData("DA40_STBY_ALTIMETER_UP",
        "(L:KOHLSMAN SETTING HG:2) 0.01 + 31.5 min (>L:KOHLSMAN SETTING HG:2)")]
    [InlineData("DA40_STBY_ALTIMETER_DN",
        "(L:KOHLSMAN SETTING HG:2) 0.01 - 28 max (>L:KOHLSMAN SETTING HG:2)")]
    public void EachDetentIsTheAircraftsOwnRpnIncludingItsClamps(string key, string rpn)
    {
        // ⚠️ The clamps (31.5 and 28) belong to the MODEL. Rewriting this expression - even
        // "equivalently" - is how a step stops matching what the knob does, and the aeroplane
        // is the only authority on where its own subscale stops.
        string src = StandbySource();
        int at = src.IndexOf($"case \"{key}\":", StringComparison.Ordinal);
        Assert.True(at >= 0, $"{key} has no case - the button is dead");
        string body = src.Substring(at, Math.Min(600, src.Length - at));
        Assert.Contains(rpn, body);
    }

    [Theory]
    [InlineData("DA40_STBY_ALTIMETER_UP")]
    [InlineData("DA40_STBY_ALTIMETER_DN")]
    public void ItGoesThroughTheCalculatorUniquelyAndNeverSetLVar(string key)
    {
        // ⚠️ TWO TRAPS AT ONCE. The input's name carries a SPACE AND A COLON, so SetLVar
        // refuses the calc path and its data-def fallback lands on the STOCK SimVar of that
        // name - a different variable entirely. And a second detent in the same direction is
        // a byte-identical calc string, which MobiFlight drops, so every other click of a
        // sweep would go missing without the unique form.
        string src = StandbySource();
        int at = src.IndexOf($"case \"{key}\":", StringComparison.Ordinal);
        string body = src.Substring(at, Math.Min(600, src.Length - at));
        Assert.Contains("ExecuteCalculatorCodeUnique", body);
        Assert.DoesNotContain("SetLVar", body);
    }

    [Fact]
    public void ADetentNeverAnnouncesForItselfBecauseTheSettleAlreadyDoes()
    {
        // A detent is one of a burst. The baro settle announcer already waits for the knob to
        // stop and speaks the resting value in BOTH units; announcing per detent would read a
        // sweep as forty numbers - the recital rule the radio read-back exists to enforce.
        string src = StandbySource();
        int at = src.IndexOf("case \"DA40_STBY_ALTIMETER_UP\":", StringComparison.Ordinal);
        int end = src.IndexOf("case \"DA40_STBY_GYRO_CAGE\":", StringComparison.Ordinal);
        Assert.True(at >= 0 && end > at);
        Assert.DoesNotContain("announcer.Announce", src.Substring(at, end - at));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void BothDetentsSitBesideTheTypedBoxOnBothVariants(DA40Variant variant)
    {
        // The standby instruments are the same on the NG and the XLS.
        var rows = new CowsDA40Definition(variant).GetPanelControls()["Standby Instruments"];
        int at = rows.IndexOf("DA40_STBY_ALTIMETER_SET");
        Assert.True(at >= 0, "the typed box is missing");
        Assert.Equal("DA40_STBY_ALTIMETER_UP", rows[at + 1]);
        Assert.Equal("DA40_STBY_ALTIMETER_DN", rows[at + 2]);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheTwoDeadRadioDuplicatesAreGone(DA40Variant variant)
    {
        // ⚠️ They were DEFINED with live setter cases but sat in no panel, no display list and
        // no hotkey, so a pilot could never reach them - two SimConnect definitions burning
        // budget while duplicating NAV OBS:1 and AUTOPILOT HEADING LOCK DIR, which is the
        // batch-collision shape. Course and heading live on the GFC 700 and always did.
        var vars = new CowsDA40Definition(variant).GetVariables();
        Assert.False(vars.ContainsKey("DA40_RADIO_CRS1_SET"));
        Assert.False(vars.ContainsKey("DA40_RADIO_HDG_BUG_SET"));
        Assert.True(vars.ContainsKey("DA40_AP_CRS_SET"));
        Assert.True(vars.ContainsKey("DA40_AP_HDG_SET"));
    }
}
