using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The two altimeters: what they read, how they are set, and what Ctrl+B says about them.
/// </summary>
public class CowsDA40StandbyBaroKnobTests
{
    [Fact]
    public void TheBackupAltitudeIsTheStandbyNeedleAndNotTheMainAltimeter()
    {
        // ⚠️ IT READ THE STOCK "INDICATED ALTITUDE", which the sim drives from the G1000's
        // subscale - so the row labelled "Backup Altitude" reported the instrument the pilot
        // had NOT touched, and setting the standby subscale had no readable consequence
        // anywhere in MSFSBA. Two altimeters disagreeing is the entire reason the aeroplane
        // carries a standby, and the disagreement was invisible.
        //
        // Measured live, ten clicks of the standby subscale on the ground:
        //     L:PRESSURE_ALT_INDI   67.8 -> 155.5 ft
        //     A:INDICATED ALTITUDE  47.3 ->  47.3 ft   (unchanged)
        var d = new CowsDA40Definition(DA40Variant.NG).GetVariables()["DA40_STBY_ALTITUDE"];
        Assert.Equal("PRESSURE_ALT_INDI", d.Name);
        Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.LVar, d.Type);

        // ⚠️ AND ITS UNIT MUST STAY "number". An L:var registered with a converting unit
        // makes SimConnect convert from its own base unit and return garbage. The word a
        // pilot HEARS comes from the display override instead - which is why the row read
        // "1453 number" the moment this moved off the stock SimVar.
        Assert.Equal("number", d.Units);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void BothAltimetersAreSeparatelyTunableFromThePanel(DA40Variant variant)
    {
        // ⚠️ THE MAIN ALTIMETER HAD NO PANEL CONTROL AT ALL - only Ctrl+B, which set BOTH
        // together, and the display window's knob keys. So the one thing a standby exists
        // for could not be done from the panels: set them differently, or notice they
        // already are.
        var rows = new CowsDA40Definition(variant).GetPanelControls()["Standby Instruments"];
        Assert.Contains("DA40_G1000_BARO_SET", rows);
        Assert.Contains("DA40_STBY_ALTIMETER_SET", rows);
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void NeitherAltimeterCarriesStepButtonsOnThePanel(DA40Variant variant)
    {
        // ⚠️ THEY WERE ADDED AND THEN REMOVED ON THE PILOT'S RULING, and the reasoning is
        // worth keeping because it is not "buttons are bad": if you can TYPE the value you
        // can just type it, and four extra buttons on a seven-row panel is clutter every
        // pilot tabs through forever to reach the two rows that do the work. The knob feel
        // still exists where it belongs - the PFD bezel keys in the display window.
        //
        // ⚠️ NOT A GENERAL REPEAL. The GFC 700's ten step buttons stay, because there the
        // panel is the ONLY way to step those five values - no bezel key reaches them. The
        // rule: a step button earns its place only where nothing else can turn that knob.
        var def = new CowsDA40Definition(variant);
        var rows = def.GetPanelControls()["Standby Instruments"];
        foreach (string gone in new[]
        {
            "DA40_STBY_ALTIMETER_UP", "DA40_STBY_ALTIMETER_DN",
            "DA40_G1000_BARO_UP", "DA40_G1000_BARO_DN"
        })
        {
            Assert.DoesNotContain(gone, rows);
            Assert.False(def.GetVariables().ContainsKey(gone), $"{gone} is a dead definition");
        }

        // The GFC 700 half, so the two rulings can never be confused for one.
        Assert.Contains("DA40_AP_CRS_UP", def.GetPanelControls()["GFC 700"]);
    }

    [Fact]
    public void CtrlBSaysBothOnceWhenTheAltimetersAgree()
    {
        Assert.Equal("Both altimeters set, 1013 hectopascals, 29.92 inches",
            CowsDA40Definition.BaroSetPhrase(29.92, 29.92));
    }

    [Fact]
    public void CtrlBNamesEachAltimeterWhenTheyDisagree()
    {
        // ⚠️ A DISAGREEMENT IS NAMED, NEVER AVERAGED OR SUMMARISED. Two altimeters set apart
        // is either deliberate or a mistake, and both readings must be spoken for the pilot
        // to tell which - "Both altimeters set" over two different numbers would hide exactly
        // the state the standby exists to reveal.
        string said = CowsDA40Definition.BaroSetPhrase(29.92, 30.12);
        Assert.Contains("Main altimeter", said);
        Assert.Contains("29.92", said);
        Assert.Contains("Standby altimeter", said);
        Assert.Contains("30.12", said);
        Assert.DoesNotContain("Both altimeters", said);
    }

    [Fact]
    public void ARoundingDifferenceIsNotADisagreement()
    {
        // A hundredth of an inch is the knob's own detent, so anything smaller is rounding
        // rather than a real difference - and reading two nearly identical numbers back at
        // a pilot who set one value is noise.
        Assert.StartsWith("Both altimeters set",
            CowsDA40Definition.BaroSetPhrase(29.920, 29.923));
    }
}
