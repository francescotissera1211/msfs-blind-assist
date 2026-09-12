using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE XLS READS THE INDICATION, AND THE NG'S RPM RULE MUST NOT BE INHERITED.
///
/// DISP_* IS what the pilot reads: COWS models an instrument failure as FAILURES_DISP_*,
/// which zeroes the drawn value while the physics carries on. So a readout bound to the
/// physics shows a blind pilot a perfect number off a dead gauge - the exact failure class
/// COWS deliberately modelled, and one a sighted pilot cannot be fooled by.
///
/// The tachometer is where the two variants genuinely differ, and it was flagged as
/// unresolved for months. Counted in both model directories of the installed package:
///
///     NG    DISP_PROP_RPM ✓   PROP_RPM_SENS ✓   FAILURES_DISP_RPM ✗
///     XLS   DISP_PROP_RPM ✓   PROP_RPM_SENS ✗   FAILURES_DISP_RPM ✓  (7 references)
///
/// So the NG reads the SENSOR because its gauge cannot fail and DISP_PROP_RPM is quantised
/// to 10 RPM there; the XLS must read the GAUGE because it can. Injected live on the XLS:
/// FAILURES_DISP_RPM = 1 took DISP_PROP_RPM from 1020 to ZERO while (A:GENERAL ENG RPM:1,
/// rpm) went on reading 1013. Same proved for MAP: DISP_MAP 15.13 to zero while TB_CALC_MAP
/// held 0.51 bar.
/// </summary>
public class CowsDA40XlsIndicationTests
{
    [Theory]
    [InlineData("DA40_XLS_RPM", "DISP_PROP_RPM")]
    [InlineData("DA40_XLS_MAP", "DISP_MAP")]
    [InlineData("DA40_XLS_OIL_PRESSURE", "DISP_OP")]
    [InlineData("DA40_XLS_FUEL_FLOW", "DISP_FF")]
    [InlineData("DA40_XLS_CHT_1", "DISP_CHT:1")]
    [InlineData("DA40_XLS_EGT_1", "DISP_EGT:1")]
    public void EngineReadoutsComeFromTheIndication(string key, string expected)
    {
        var vars = new CowsDA40Definition(DA40Variant.XLS).GetVariables();
        Assert.Equal(expected, vars[key].Name);
    }

    /// <summary>
    /// The NG keeps its sensor, for the reason above. Pinned beside the XLS rule so the two
    /// can never be "harmonised" into one.
    /// </summary>
    [Fact]
    public void TheNgKeepsItsSensorBecauseItsTachometerCannotFail()
    {
        var vars = new CowsDA40Definition(DA40Variant.NG).GetVariables();
        Assert.Equal("PROP_RPM_SENS:1", vars["DA40_POWER_RPM"].Name);
    }

    /// <summary>
    /// ⚠️ TWO READOUTS ARE KNOWINGLY STILL ON THE PHYSICS, AND THIS RECORDS WHY RATHER THAN
    /// PRETENDING THEY ARE DONE. Oil temperature's indication is in FAHRENHEIT while its arc
    /// table is CELSIUS (65 / 110 / 118), and fuel pressure's indication is PSI while its
    /// arcs are BAR (0.965 / 2.413). A band is looked up from the RAW value, so swapping the
    /// source without moving the arcs in the same change would put a green needle in the red.
    /// Both need a change that can be flown, not arithmetic.
    /// </summary>
    [Theory]
    [InlineData("DA40_XLS_OIL_TEMP", "GENERAL ENG OIL TEMPERATURE:1")]
    [InlineData("DA40_XLS_FUEL_PRESSURE", "ENG_FUEL_PRESS")]
    public void TheTwoUnitMismatchedReadoutsAreStillOnThePhysics(string key, string expected)
    {
        var vars = new CowsDA40Definition(DA40Variant.XLS).GetVariables();
        Assert.Equal(expected, vars[key].Name);
    }
}
