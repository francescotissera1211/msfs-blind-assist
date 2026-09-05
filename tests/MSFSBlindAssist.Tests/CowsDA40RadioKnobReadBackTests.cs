using MSFSBlindAssist.Forms.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ Turning a radio knob read the frequency from BEFORE the keystroke.
///
/// The handler fired the event and scraped in the same breath, so the display had not had a
/// frame: the row looked unchanged, nothing was announced, and the pilot's NEXT press read
/// back the PREVIOUS one's frequency. Reported as "it was at 710, I pressed twice, I got 715"
/// - 715 was the first press, and the radio was already on 725 by the time it was spoken.
///
/// ⚠️ Note what is NOT a bug in that report: 710 to 715 to 725 to 730 is CORRECT 8.33 kHz
/// stepping. The channel names are not evenly spaced - 720, 745, 770 and 795 are not
/// channels at all - so the radio steps over them and the gap is real.
/// </summary>
public class CowsDA40RadioKnobReadBackTests
{
    [Fact]
    public void TheRadioThatMovedIsTheOneSpoken()
    {
        string before = "NAV 1, active 116.70, standby 110.50 | COM 1, active 127.850, standby 121.710";
        string after = "NAV 1, active 116.70, standby 110.50 | COM 1, active 127.850, standby 121.715";

        Assert.Equal("COM 1, active 127.850, standby 121.715",
            CowsDA40DisplayForm.FirstChangedRadio(before, after));
    }

    [Fact]
    public void TheFlagsAreDroppedBecauseAKnobStepDoesNotMoveThem()
    {
        // TUNING and TRANSMIT are on every press and answer a question nobody asked while
        // tuning; the frequency is the answer.
        string before = "COM 1, active 127.850, standby 121.710, TUNING, TRANSMIT";
        string after = "COM 1, active 127.850, standby 121.715, TUNING, TRANSMIT";

        string said = CowsDA40DisplayForm.FirstChangedRadio(before, after);
        Assert.DoesNotContain("TUNING", said);
        Assert.DoesNotContain("TRANSMIT", said);
        Assert.Contains("121.715", said);
    }

    [Fact]
    public void NothingChangedStaysSILENTRatherThanRepeatingTheOldValue()
    {
        // ⚠️ THE SPECIFIC LIE THIS EXISTS TO STOP TELLING. Re-speaking the unchanged value
        // is what made a press that had not landed yet sound like a press that had.
        string same = "COM 1, active 127.850, standby 121.710";
        Assert.Equal("", CowsDA40DisplayForm.FirstChangedRadio(same, same));
    }

    [Fact]
    public void AnEmptyReadIsSilentToo()
    {
        // The socket can answer with nothing while the display is between frames; that is
        // not a frequency and must never be announced as one.
        Assert.Equal("", CowsDA40DisplayForm.FirstChangedRadio("COM 1, standby 121.710", ""));
    }

    [Fact]
    public void ASecondRadioMovingIsFoundEvenWhenTheFirstDidNot()
    {
        string before = "NAV 1, active 116.70, standby 110.50 | NAV 2, active 110.50, standby 113.90";
        string after = "NAV 1, active 116.70, standby 110.50 | NAV 2, active 110.50, standby 113.95";

        Assert.Contains("113.95", CowsDA40DisplayForm.FirstChangedRadio(before, after));
        Assert.Contains("NAV 2", CowsDA40DisplayForm.FirstChangedRadio(before, after));
    }

    [Theory]
    // The 8.33 channel names that DO exist, so a future "fix" cannot decide the gaps are bugs.
    [InlineData("121.705")]
    [InlineData("121.710")]
    [InlineData("121.715")]
    [InlineData("121.725")]
    [InlineData("121.730")]
    public void TheEightThirtyThreeChannelsInThatSequenceAreAllReportedVerbatim(string freq)
    {
        string before = "COM 1, active 127.850, standby 118.000";
        string after = $"COM 1, active 127.850, standby {freq}";
        Assert.Contains(freq, CowsDA40DisplayForm.FirstChangedRadio(before, after));
    }
}
