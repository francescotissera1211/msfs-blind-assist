using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

internal static partial class GroundTrafficLogic
{
    private static readonly Regex RxCallsign = new(@"^([A-Z]{2,4})(\d{1,5}[A-Z]{0,2})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A flight's callsign: the airline's three-letter ICAO code, then the flight number.
    private static readonly Regex RxAirlineCallsign = new(@"^([A-Z]{3})\d", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Two to four letters and digits, a letter first: the shape of an ICAO type designator.
    private static readonly Regex RxDesignator = new(@"^[A-Z][A-Z0-9]{1,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// An aircraft type the way a pilot says it ("B77W" → "777", "E55P" → "Phenom 300"): its name in
    /// <c>SpokenTypes</c>, else the designator as it is written. "" for no type, and for a placeholder
    /// that names none ("ZZZZ").
    /// </summary>
    public static string SpokenType(string? rawType)
    {
        if (SpokenTypeName(rawType) is { } whole) return whole;
        string icao = Forms.TcasForm.ShortenAircraftType(rawType ?? "");
        if (string.IsNullOrEmpty(icao)) return "";
        if (SpokenTypeName(icao) is { } spoken) return spoken;
        // B747 style: already speakable; drop the Boeing B.
        if (Regex.IsMatch(icao, @"^B7\d7$", RegexOptions.CultureInvariant)) return icao[1..];
        return icao;
    }

    private static readonly Regex RxWord = new(@"[A-Za-z]{3,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A type as a LIST shows it (the traffic window): its spoken name where the designator is opaque
    /// ("E55P" → "Phenom 300", "DH8D" → "Dash 8", "B38M" → "737 MAX"), the designator itself where it
    /// already reads as what it is and says more than the family name does ("B738", "A20N", "MD82",
    /// "CRJ9"): a list is read at leisure, and the variant is worth keeping there. "" for no type.
    /// </summary>
    public static string ListedType(string? rawType)
    {
        string designator = Forms.TcasForm.ShortenAircraftType(rawType ?? "");
        string spoken = SpokenType(rawType);
        if (spoken.Length == 0 || designator.Length == 0) return spoken.Length > 0 ? spoken : designator;
        bool namesIt = RxWord.IsMatch(spoken) && !designator.StartsWith(spoken, StringComparison.OrdinalIgnoreCase);
        return namesIt ? spoken : designator;
    }

    /// <summary>
    /// The type a traffic aircraft is NAMED by, from everything known about it, best first: the type its
    /// title or its ATC strings name (<see cref="GroundTrafficAircraftSizes.ResolveIcao"/>); the designator
    /// its own package declares (<paramref name="declaredIcao"/>, <c>icao_type_designator</c> in its
    /// aircraft.cfg) or the type the simulator reports, whichever has a spoken name; a title that is itself
    /// a designator (IVAO's "A5"); then the declared designator as it is written, so a type nobody has
    /// named here is still read as its code and never as its maker ("Panavia", "Sikorsky": what AIG and
    /// IVAO put in the ATC type).
    /// </summary>
    public static string NamingType(string? receivedType, string? atcModel, string? title, string? declaredIcao)
    {
        string icao = GroundTrafficAircraftSizes.ResolveIcao(receivedType, atcModel, title);
        if (icao.Length > 0) return icao;
        string declared = (declaredIcao ?? "").Trim().ToUpperInvariant(), received = (receivedType ?? "").Trim(), t = (title ?? "").Trim();
        if (SpokenTypeName(declared) != null) return declared;
        if (SpokenTypeName(received) != null) return received;
        if (SpokenTypeName(t) != null) return t;
        return RxDesignator.IsMatch(declared) ? declared : received;
    }

    /// <summary>
    /// The airline to name a traffic aircraft by: the one its model gives (<c>ATC AIRLINE</c>); else, for
    /// a model that gives none (every IVAO model, a plain livery), the name the INSTALLED liveries give
    /// the airline code its package declares, or the code its callsign begins with ("RYR123"):
    /// <paramref name="airlineByCode"/>, <c>GroundTrafficModelCatalog.AirlineName</c>. "" when none is known.
    /// </summary>
    public static string SpokenAirline(string? modelAirline, string? declaredAirlineCode, string? callsign, Func<string, string>? airlineByCode)
    {
        string airline = (modelAirline ?? "").Trim();
        if (airline.Length > 0 || airlineByCode == null) return airline;
        string code = (declaredAirlineCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 3 && airlineByCode(code) is { Length: > 0 } byPackage) return byPackage;
        var m = RxAirlineCallsign.Match((callsign ?? "").Trim().ToUpperInvariant());
        return m.Success ? airlineByCode(m.Groups[1].Value) ?? "" : "";
    }

    /// <summary>
    /// A callsign spaced for speech ("DAL123" → "DAL 123", "EZY45MR" → "EZY 45MR"). The ONE callsign
    /// formatter — <c>TcasForm.FormatCallsign</c> delegates here (PR #247 review L8). Registrations
    /// (N12345), and anything already containing a space or a hyphen, come back trimmed but unchanged;
    /// null or blank comes back "".
    /// </summary>
    public static string SpokenCallsign(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw.Trim();
        if (raw.Contains(' ') || raw.Contains('-')) return raw;
        var m = RxCallsign.Match(raw.ToUpperInvariant());
        return m.Success ? $"{m.Groups[1].Value} {m.Groups[2].Value}" : raw;
    }

    /// <summary>
    /// How to name an aircraft in a callout, the way ATC would: "Delta A320" when the airline is
    /// known, else "DAL 123, A320", else the type alone, else "traffic".
    /// </summary>
    public static string SpokenName(string? airline, string? callsign, string? rawType)
    {
        string type = SpokenType(rawType);
        string air = (airline ?? "").Trim();
        if (air.Length > 0)
            return type.Length > 0 ? $"{air} {type}" : $"{air} traffic";
        string cs = SpokenCallsign(callsign);
        if (cs.Length > 0)
            return type.Length > 0 ? $"{cs}, {type}" : cs;
        return type.Length > 0 ? type : "traffic";
    }

    /// <summary>
    /// The Alt+G summary's name: the callout name plus the spaced callsign when the airline hid it
    /// ("Delta A320, DAL 1234") — at a hub several "Delta A320"s are otherwise indistinguishable, and
    /// ATC addresses them by callsign (PR #247 review L3). Callouts keep the short name.
    /// </summary>
    public static string SpokenNameWithCallsign(string? airline, string? callsign, string? rawType)
    {
        string name = SpokenName(airline, callsign, rawType);
        string cs = SpokenCallsign(callsign);
        return (airline ?? "").Trim().Length > 0 && cs.Length > 0 ? $"{name}, {cs}" : name;
    }

    /// <summary>
    /// Rebuild a name that has no type once a type is known — the VATSIM feed loads lazily, so the
    /// first lookup for a new callsign often returns "" (PR #247 review L3).
    /// </summary>
    public static bool NameNeedsRefresh(bool nameHasType, string? resolvedType)
        => !nameHasType && SpokenType(resolvedType).Length > 0;
}
