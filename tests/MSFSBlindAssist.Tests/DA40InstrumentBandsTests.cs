using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The DA40's gauge arcs, from AFM section 2.5. These matter because the band IS the
/// reading for a sighted pilot — a number without its arc is less than the glance gives
/// everyone else. Boundary values are pinned deliberately: the AFM writes green as
/// "50° to 135°C" with 135-140 caution, so a value exactly on a boundary belongs to the
/// higher band.
/// </summary>
public class DA40InstrumentBandsTests
{
    private static GaugeBand Band(string key, double v) => DA40InstrumentBands.For(key)!.Classify(v);

    [Theory]
    [InlineData(-40, GaugeBand.LowerRed)]      // below -30
    [InlineData(20, GaugeBand.LowerCaution)]   // -30 to 50
    [InlineData(87, GaugeBand.Normal)]         // the value measured on the live aircraft
    [InlineData(137, GaugeBand.UpperCaution)]  // 135 to 140
    [InlineData(145, GaugeBand.UpperRed)]      // above 140
    public void OilTemperatureArcs(double value, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_START_OIL_TEMP", value));

    [Theory]
    [InlineData(0.5, GaugeBand.LowerRed)]      // below 0.9 bar
    [InlineData(1.5, GaugeBand.LowerCaution)]  // 0.9 to 2.5
    [InlineData(3.5, GaugeBand.Normal)]        // 2.5 to 6.0
    [InlineData(6.2, GaugeBand.UpperCaution)]  // 6.0 to 6.5
    [InlineData(7.0, GaugeBand.UpperRed)]      // above 6.5
    public void OilPressureArcs(double value, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_START_OIL_PRESSURE", value));

    [Theory]
    [InlineData(700, GaugeBand.Normal)]        // idle
    [InlineData(2100, GaugeBand.Normal)]       // max continuous, still green
    [InlineData(2200, GaugeBand.UpperCaution)] // take-off range
    [InlineData(2400, GaugeBand.UpperRed)]     // above 2300
    public void PropellerRpmArcs(double value, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_START_RPM", value));

    [Theory]
    [InlineData(23.0, GaugeBand.LowerRed)]
    [InlineData(24.5, GaugeBand.LowerCaution)]
    [InlineData(27.8, GaugeBand.Normal)]       // measured live with the alternator on line
    [InlineData(31.0, GaugeBand.UpperCaution)]
    [InlineData(33.0, GaugeBand.UpperRed)]
    public void VoltmeterArcs(double value, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_ELEC_DISP_VOLTS", value));

    [Theory]
    [InlineData(28, GaugeBand.Normal)]
    [InlineData(65, GaugeBand.UpperCaution)]
    [InlineData(75, GaugeBand.UpperRed)]
    public void AmmeterHasNoLowerArcs(double value, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_ELEC_DISP_AMPS", value));

    // ---- DA40-XLS: the Lycoming's arcs, AFM 6.01.01-E section 2.5 ----

    [Theory]
    [InlineData(2000, GaugeBand.Normal)]       // the run-up figure
    [InlineData(2400, GaugeBand.Normal)]       // top of green, still green
    [InlineData(2500, GaugeBand.UpperCaution)] // 2400-2700 is yellow on the Lycoming, red on the Austro
    [InlineData(2750, GaugeBand.UpperRed)]     // above 2700
    public void XlsPropellerRpmArcs(double value, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_XLS_RPM", value));

    [Theory]
    [InlineData(20, GaugeBand.LowerRed)]       // below the 25 psi idle minimum
    [InlineData(40, GaugeBand.LowerCaution)]   // 25-55
    [InlineData(73.7, GaugeBand.Normal)]       // measured running, cold, at EGNX
    [InlineData(96, GaugeBand.UpperCaution)]   // 96-97
    [InlineData(100, GaugeBand.UpperRed)]      // above 97
    public void XlsOilPressureArcs(double value, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_XLS_OIL_PRESSURE", value));

    [Theory]
    [InlineData(19, GaugeBand.LowerCaution)]   // the cold engine, measured at ambient - below green, not red
    [InlineData(85, GaugeBand.Normal)]         // 149-230 F
    [InlineData(115, GaugeBand.UpperCaution)]  // 231-245 F
    [InlineData(120, GaugeBand.UpperRed)]      // above 245 F / 118 C
    public void XlsOilTemperatureArcs(double value, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_XLS_OIL_TEMP", value));

    [Theory]
    [InlineData(0.5, GaugeBand.LowerCaution)]
    [InlineData(10.6, GaugeBand.Normal)]       // measured at 2200 rpm
    [InlineData(25, GaugeBand.UpperCaution)]
    public void XlsFuelFlowArcs(double value, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_XLS_FUEL_FLOW", value));

    [Theory]
    [InlineData(0.5, GaugeBand.LowerRed)]      // 7 psi - below the 14 psi minimum
    [InlineData(1.616, GaugeBand.Normal)]      // measured: 23.4 psi
    [InlineData(2.6, GaugeBand.UpperRed)]      // 38 psi - above the 35 psi maximum
    public void XlsFuelPressureArcsAreInBar(double bar, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_XLS_FUEL_PRESSURE", bar));

    [Theory]
    [InlineData(410, GaugeBand.Normal)]        // measured at run-up, hottest cylinder
    [InlineData(480, GaugeBand.UpperCaution)]  // the plugin's 475-500 F caution band
    [InlineData(505, GaugeBand.UpperRed)]      // above 500 F
    public void XlsCylinderHeadArcsAreThePluginsFahrenheitBands(double f, GaugeBand expected)
    {
        Assert.Equal(expected, Band("DA40_XLS_CHT_HOT", f));
        Assert.Equal(expected, Band("DA40_XLS_CHT_1", f));
    }

    [Theory]
    [InlineData(1310, GaugeBand.Normal)]
    [InlineData(1360, GaugeBand.UpperCaution)] // over the POH's recommended 1350 F maximum
    public void XlsExhaustArcsCarryOnlyThePohsRecommendedMaximum(double f, GaugeBand expected)
        => Assert.Equal(expected, Band("DA40_XLS_EGT_HOT", f));

    [Fact]
    public void LoadHasNoUpperRed_BecauseTheAfmDefinesNone()
    {
        // Max continuous 92 %, take-off 100 %, and the table's upper-red cell is empty.
        Assert.Equal(GaugeBand.Normal, Band("DA40_START_LOAD", 80));
        Assert.Equal(GaugeBand.UpperCaution, Band("DA40_START_LOAD", 96));
        Assert.Equal(GaugeBand.UpperCaution, Band("DA40_START_LOAD", 150));
    }

    [Fact]
    public void GearboxArcsMatchTheEcuTestPrecondition()
    {
        // The ECU test wants 35 C minimum; below that the gauge is in the lower caution.
        Assert.Equal(GaugeBand.LowerCaution, Band("DA40_START_GEARBOX_TEMP", 30));
        Assert.Equal(GaugeBand.Normal, Band("DA40_START_GEARBOX_TEMP", 82));
        Assert.Equal(GaugeBand.UpperRed, Band("DA40_START_GEARBOX_TEMP", 125));
    }

    [Fact]
    public void AnnotateAppendsTheArcToTheReading()
    {
        Assert.Equal("87 celsius, green",
            DA40InstrumentBands.Annotate("DA40_START_OIL_TEMP", 87, "87 celsius"));
        Assert.Equal("145 celsius, red, above maximum",
            DA40InstrumentBands.Annotate("DA40_START_OIL_TEMP", 145, "145 celsius"));
    }

    [Fact]
    public void AnnotateLeavesGaugesWithoutArcsAlone()
    {
        // Applied unconditionally by the caller, so it must be a no-op for the rest.
        Assert.Equal("29 celsius",
            DA40InstrumentBands.Annotate("DA40_ICE_OAT", 29, "29 celsius"));
        Assert.Null(DA40InstrumentBands.For("DA40_ICE_OAT"));
    }

    [Fact]
    public void EveryAnnotatedKeyIsARealVariable()
    {
        // An arc attached to a key that no longer exists would silently never fire. The
        // table carries both airframes' gauges - the Austro's and the Lycoming's arcs
        // differ, so they are separate keys - and a key is real if EITHER variant defines it.
        var ng = new CowsDA40Definition(DA40Variant.NG).GetVariables();
        var xls = new CowsDA40Definition(DA40Variant.XLS).GetVariables();

        foreach (var key in DA40InstrumentBands.AnnotatedKeys)
        {
            Assert.True(ng.ContainsKey(key) || xls.ContainsKey(key),
                $"{key} has arcs but is not a defined variable on either variant");
        }
    }
}
