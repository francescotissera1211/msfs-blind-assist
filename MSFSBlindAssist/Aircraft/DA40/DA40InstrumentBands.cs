namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>Which arc of a gauge a value is sitting in.</summary>
public enum GaugeBand
{
    /// <summary>Lower prohibited range — red arc below the scale's usable region.</summary>
    LowerRed,
    /// <summary>Lower caution range — yellow arc below green.</summary>
    LowerCaution,
    /// <summary>Normal operating range — green arc.</summary>
    Normal,
    /// <summary>Upper caution range — yellow arc above green.</summary>
    UpperCaution,
    /// <summary>Upper prohibited range — red arc above the scale.</summary>
    UpperRed
}

/// <summary>
/// One gauge's arcs, as boundaries between them. A null boundary means that arc does not
/// exist on this instrument — the RPM gauge has no lower red or lower caution at all, and
/// Load has no upper red.
///
/// Boundaries follow the AFM's own wording: a value exactly ON a boundary belongs to the
/// HIGHER band, because the table reads "50° to 135°C" for green with 135-140 as caution.
/// </summary>
public sealed record BandRange(
    double? LowerRedBelow,
    double? GreenFrom,
    double? GreenTo,
    double? UpperRedFrom)
{
    public GaugeBand Classify(double value)
    {
        if (LowerRedBelow.HasValue && value < LowerRedBelow.Value) return GaugeBand.LowerRed;
        if (GreenFrom.HasValue && value < GreenFrom.Value) return GaugeBand.LowerCaution;
        if (GreenTo.HasValue && value > GreenTo.Value)
        {
            if (UpperRedFrom.HasValue && value > UpperRedFrom.Value) return GaugeBand.UpperRed;
            return GaugeBand.UpperCaution;
        }
        return GaugeBand.Normal;
    }
}

/// <summary>
/// The DA40-NG's instrument arcs, transcribed from AFM section 2.5 ENGINE INSTRUMENT
/// MARKINGS and section 2.3 AIRSPEED INDICATOR MARKINGS.
///
/// This exists because a sighted pilot does not read "87 degrees" off the oil temperature
/// gauge — they see the needle sitting in the green. Reporting only the number gives a
/// blind pilot strictly less than the glance gives everyone else, so every gauge with
/// published arcs reports its band alongside its value.
///
/// The table, verbatim from the AFM:
///
///   Indication     lower red      lower caution   green          upper caution   upper red
///   RPM            --             --              up to 2100     2100-2300       above 2300
///   Oil pressure   below 0.9 bar  0.9-2.5         2.5-6.0        6.0-6.5         above 6.5
///   Oil temp       below -30 C    -30 to 50       50-135         135-140         above 140
///   Coolant temp   below -30 C    -30 to 60       60-100         100-105         above 105
///   Gearbox temp   below -30 C    -30 to 35       35-115         115-120         above 120
///   Load           --             --              up to 92%      92-100%         --
///   Fuel temp      below -25 C    -25 to -20      -20 to 55      55-60           above 60
///   Ammeter        --             --              up to 60 A     60-70           above 70
///   Voltmeter      below 24.1 V   24.1-25         25-30          30-32           above 32
///   Fuel quantity  below 1 USG    --              1-14 USG       --              --
///
/// Airspeed (section 2.3): green 66-130 KIAS, yellow 130-172 (smooth air only),
/// red line 172 KIAS = Vne.
/// </summary>
public static class DA40InstrumentBands
{
    /// <summary>Arcs keyed by the MSFSBA variable key they annotate.</summary>
    private static readonly Dictionary<string, BandRange> Bands = new()
    {
        // Propeller RPM. No lower arcs; max continuous 2100, take-off 2300.
        ["DA40_START_RPM"]            = new BandRange(null, null, 2100, 2300),
        ["DA40_ECU_PROP_SENSED"]      = new BandRange(null, null, 2100, 2300),
        ["DA40_POWER_RPM"]            = new BandRange(null, null, 2100, 2300),

        // Oil pressure, bar.
        ["DA40_START_OIL_PRESSURE"]   = new BandRange(0.9, 2.5, 6.0, 6.5),

        // Oil temperature, degrees C.
        ["DA40_START_OIL_TEMP"]       = new BandRange(-30, 50, 135, 140),

        // Coolant temperature, degrees C.
        ["DA40_START_COOLANT_TEMP"]   = new BandRange(-30, 60, 100, 105),

        // Gearbox temperature, degrees C. Note the AFM says the yellow arc here is not an
        // engine-manufacturer limit — it was added purely to draw attention to the
        // approaching maximum, and carries no time limit.
        ["DA40_START_GEARBOX_TEMP"]   = new BandRange(-30, 35, 115, 120),
        ["DA40_ECU_PRE_GEARBOX"]      = new BandRange(-30, 35, 115, 120),

        // Engine load, percent. Max continuous 92, take-off 100. No upper red.
        ["DA40_START_LOAD"]           = new BandRange(null, null, 92, null),
        ["DA40_POWER_LOAD"]           = new BandRange(null, null, 92, null),

        // Electrical. Ammeter has no lower arcs; voltmeter has the full set.
        ["DA40_ELEC_DISP_AMPS"]       = new BandRange(null, null, 60, 70),
        ["DA40_ELEC_DISP_VOLTS"]      = new BandRange(24.1, 25, 30, 32),
        ["DA40_START_VOLTS"]          = new BandRange(24.1, 25, 30, 32)
    };

    /// <summary>The arcs for a variable, or null when that gauge has no published arcs.</summary>
    public static BandRange? For(string variableKey)
        => Bands.TryGetValue(variableKey, out var range) ? range : null;

    /// <summary>Spoken name of a band, as a pilot would describe the arc.</summary>
    public static string Describe(GaugeBand band) => band switch
    {
        GaugeBand.LowerRed     => "red, below minimum",
        GaugeBand.LowerCaution => "yellow, low",
        GaugeBand.Normal       => "green",
        GaugeBand.UpperCaution => "yellow, high",
        GaugeBand.UpperRed     => "red, above maximum",
        _ => ""
    };

    /// <summary>
    /// Appends the band to an already-formatted reading, e.g. "87 celsius" becomes
    /// "87 celsius, green". Returns the text unchanged for a gauge with no arcs, so
    /// callers can apply this unconditionally.
    /// </summary>
    public static string Annotate(string variableKey, double value, string formattedValue)
    {
        var range = For(variableKey);
        if (range is null) return formattedValue;

        return $"{formattedValue}, {Describe(range.Classify(value))}";
    }

    /// <summary>Every variable key that carries arcs — used by tests and diagnostics.</summary>
    public static IReadOnlyCollection<string> AnnotatedKeys => Bands.Keys;
}
