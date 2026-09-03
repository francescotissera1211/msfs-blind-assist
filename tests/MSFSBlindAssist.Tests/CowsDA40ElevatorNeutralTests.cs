using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE DA40's ELEVATOR IS NOT CENTRED AT ZERO, AND READING IT RAW COST A PILOT DAYS.
///
/// The model's own chain: ELEVATOR_ANG_TOT = stick + ELEVATOR_MATH_NEUTRAL, and
/// ELEVATOR POSITION = angle / 30 * 100, with the neutral measured live at 4 degrees. So a
/// perfectly centred stick reads 13.3 percent, and the readout announced "13 percent nose up"
/// over it. The pilot reinstalled drivers, rebuilt sensitivity curves and finally unbound the
/// elevator axis entirely - at which point it STILL read 13.3 percent, because the aeroplane
/// was right and the app was wrong.
/// </summary>
public class CowsDA40ElevatorNeutralTests
{
    private static string Say(DA40Variant v, string key, double value)
    {
        var def = new CowsDA40Definition(v);
        Assert.True(def.TryGetDisplayOverride(key, value, out string text), $"{key} has no override");
        return text;
    }

    [Fact]
    public void TheAeroplanesOwnNeutralReadsAsCentred()
    {
        // 4/30 = 0.1333, the exact value measured with the axis unbound.
        Assert.Equal("centred", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 4.0 / 30.0));
    }

    [Fact]
    public void FullNoseUpIsStillFullNoseUp()
    {
        Assert.Contains("nose up", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 1.0));
        Assert.Contains("at the stop", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 1.0));
    }

    [Fact]
    public void FullNoseDownIsStillFullNoseDown()
    {
        Assert.Contains("nose down", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", -1.0));
        Assert.Contains("at the stop", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", -1.0));
    }

    [Fact]
    public void BelowNeutralIsNoseDownRatherThanASmallerNoseUp()
    {
        // The trap the offset creates: a raw 0.05 is BELOW the aeroplane's neutral, so the
        // stick is pushed FORWARD even though the number is positive.
        Assert.Contains("nose down", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 0.05));
    }

    [Fact]
    public void AileronAndRudderAreGenuinelyZeroCentredAndUntouched()
    {
        // Only the elevator carries the offset. Applying it to the others would invent one.
        Assert.Equal("centred", Say(DA40Variant.NG, "DA40_CTL_AILERON", 0.0));
        Assert.Equal("centred", Say(DA40Variant.NG, "DA40_CTL_RUDDER", 0.0));
        Assert.Contains("right wing down", Say(DA40Variant.NG, "DA40_CTL_AILERON", 0.5));
        Assert.Contains("left", Say(DA40Variant.NG, "DA40_CTL_RUDDER", -0.5));
    }

    [Fact]
    public void TheXlsSharesTheAirframeAndTheOffset()
    {
        Assert.Equal("centred", Say(DA40Variant.XLS, "DA40_CTL_ELEVATOR", 4.0 / 30.0));
    }
}
