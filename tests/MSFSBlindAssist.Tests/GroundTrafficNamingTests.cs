// What a traffic aircraft is CALLED in a callout: its type the way it is said on the radio, for every
// type the installed traffic libraries hold, and its airline, from the model or from what the installed
// packages declare. The wording rules themselves (airline first, else callsign) are in
// GroundTrafficLogicTests.

using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Tests.GroundTrafficHarness;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficNamingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-naming-" + Guid.NewGuid().ToString("N"));

    public GroundTrafficNamingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    // Every type designator the traffic libraries on the PC this was built on resolve to (AIG, FSLTL, the
    // IVAO MTL, the stock and add-on aircraft; 30,747 titles, 2026-11-26), but one nobody could identify
    // (IVAO's "BER2", which its own aircraft.cfg does not describe either).
    private const string InstalledTypes = "A109 A124 A19N A20N A21N A225 A306 A30B A310 A318 A319 A320 A321 A332 A333 A337 A338 A339 A342 A343 A345 A346 A359 A35K A388 A3ST A4 A400 A5 A50 AA5 AB11 AEST AJET AN12 AN2 AN22 AN24 AS32 AS50 AS65 AT43 AT44 AT45 AT46 AT72 AT73 AT75 AT76 B06 B1 B190 B2 B214 B222 B350 B38M B39M B407 B461 B462 B463 B703 B712 B721 B722 B732 B733 B734 B735 B736 B737 B738 B739 B741 B742 B743 B744 B748 B74S B752 B753 B762 B763 B764 B772 B773 B77L B77W B788 B789 B78X BCS1 BCS3 BE10 BE12 BE20 BE35 BE36 BE40 BE58 BE60 BE99 BE9L BLCF BN2P BRAV BUC C101 C130 C140 C152 C160 C17 C170 C172 C177 C182 C185 C206 C207 C208 C210 C215 C25A C25B C25C C295 C337 C402 C404 C408 C414 C421 C441 C5 C510 C56X C700 C750 CC19 CH47 CL2T CL30 CL35 CL60 CN35 CONC CONI CP10 CRJ2 CRJ7 CRJ9 CRJX D328 DA40 DA42 DA62 DC10 DC3 DC6 DC91 DC93 DGF DH8A DH8B DH8C DH8D DHC2 DHC6 DHC7 DR40 E110 E120 E135 E145 E170 E175 E190 E195 E2 E290 E295 E3 E300 E35L E45X E50P E55P E737 E75L EC25 EC35 EC45 EC75 EPIC ETAR EUFI F100 F117 F14 F16 F22 F260 F28 F2TH F35A F35B F35C F4 F406 F50 F70 FA10 FA18E FA20 FA50 FA7X FA8X FDCT FOUGA G115 G21T G2CA GL5T GLEX GLF3 GLF4 GLF5 GLF6 GLID H160 H25B H53 H60 H64 HAB HAR HDJT IL38 IL76 IL78 IL96 J3 J328 JS41 JU52 K35R KFIR KODI L101 L29A L39 LJ25 LJ31 LJ35 LJ40 LJ45 LJ55 LJ60 LJ70 LJ75 M339 MD11 MD52 MD81 MD82 MD83 MD87 MD88 MD90 MG29 MI26 MI8 MIR2 MU2 P180 P210 P28A P28R P8 PA24 PA31 PA34 PA46 PAR PC12 PC21 PC24 PC6 PRM1 PTS1 Q4 R22 R44 RFAL RJ1H RJ70 RJ85 S205 S208 S61 S64 SAVG SB20 SF34 SF50 SH36 SPIT SR22 SR71 SU95 SV4 SW3 SW4 T134 T144 T154 T160 T38 TBM8 TBM9 TEX2 TOR U2 UH1 ULAC UNIV V22 VL3 VULC WW24 ZZZC ZZZZ";

    // ── The type ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_installed_type_has_a_spoken_name()
    {
        var unnamed = InstalledTypes.Split(' ').Where(t => GroundTrafficLogic.SpokenTypeName(t) == null).ToList();
        Assert.True(unnamed.Count == 0, "no spoken name: " + string.Join(" ", unnamed));
    }

    [Fact]
    public void Every_type_the_size_table_knows_has_a_spoken_name()
    {
        // A type the app can size is one it expects to meet: it is never read out as a code.
        var unnamed = GroundTrafficAircraftSizes.SizedTypes.Where(t => GroundTrafficLogic.SpokenTypeName(t) == null).OrderBy(t => t).ToList();
        Assert.True(unnamed.Count == 0, "no spoken name: " + string.Join(" ", unnamed));
    }

    [Theory]
    [InlineData("A20N", "A320")] [InlineData("A318", "A318")] [InlineData("A306", "A300")] [InlineData("BCS3", "A220")]
    [InlineData("B77W", "777")] [InlineData("B734", "737")] [InlineData("B38M", "737 MAX")] [InlineData("B722", "727")] [InlineData("B74S", "747")]
    [InlineData("MD82", "MD-80")] [InlineData("DC10", "DC-10")] [InlineData("L101", "TriStar")]
    [InlineData("CRJ2", "CRJ")] [InlineData("E145", "Embraer 145")] [InlineData("E295", "Embraer 195")] [InlineData("RJ85", "Avro RJ")]
    [InlineData("AT45", "ATR 42")] [InlineData("AT75", "ATR 72")] [InlineData("DH8C", "Dash 8")] [InlineData("DHC6", "Twin Otter")] [InlineData("SF34", "Saab 340")]
    [InlineData("E55P", "Phenom 300")] [InlineData("CL60", "Challenger 600")] [InlineData("GLEX", "Global Express")] [InlineData("GLF5", "Gulfstream 5")]
    [InlineData("C56X", "Citation Excel")] [InlineData("H25B", "Hawker")] [InlineData("LJ45", "Learjet 45")] [InlineData("FA7X", "Falcon 7X")]
    [InlineData("BE20", "King Air 200")] [InlineData("PC12", "PC-12")] [InlineData("C208", "Caravan")] [InlineData("TBM9", "TBM")]
    [InlineData("C172", "Cessna 172")] [InlineData("C182", "Cessna 182")] [InlineData("P28A", "Piper Cherokee")] [InlineData("SR22", "Cirrus SR22")] [InlineData("DA40", "Diamond DA40")]
    [InlineData("C130", "C-130")] [InlineData("A400", "A400M")] [InlineData("F16", "F-16")] [InlineData("EUFI", "Eurofighter")] [InlineData("K35R", "KC-135")]
    [InlineData("EC35", "EC135")] [InlineData("H60", "Black Hawk")] [InlineData("CH47", "Chinook")] [InlineData("R44", "Robinson R44")]
    [InlineData("A124", "Antonov 124")] [InlineData("IL76", "Ilyushin 76")] [InlineData("T154", "Tupolev 154")]
    public void A_type_is_spoken_the_way_it_is_said_on_the_radio(string designator, string spoken)
    {
        Assert.Equal(spoken, GroundTrafficLogic.SpokenType(designator));
        Assert.Equal(spoken, GroundTrafficLogic.SpokenType(designator.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("B747", "747")]            // no table entry: the Boeing B is dropped
    [InlineData("XY12", "XY12")]           // a type nobody has named is read as its code, as all were before
    [InlineData("PA-44", "Piper Seminole")]    // IVAO writes some with a hyphen
    [InlineData("Concorde", "Concorde")]
    [InlineData("Generic", "airliner")]    // the simulator's own stand-in airliners
    [InlineData("ZZZZ", "")]               // a placeholder names no type
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_type_outside_the_table_or_no_type_at_all(string? raw, string spoken)
        => Assert.Equal(spoken, GroundTrafficLogic.SpokenType(raw));

    [Fact]
    public void A_placeholder_type_leaves_the_aircraft_named_by_its_airline_or_callsign()
    {
        Assert.Equal("ZZZ 123", GroundTrafficLogic.SpokenName("", "ZZZ123", "ZZZZ"));
        Assert.Equal("Delta traffic", GroundTrafficLogic.SpokenName("Delta", "DAL1", "UNIV"));
        Assert.Equal("traffic", GroundTrafficLogic.SpokenName("", "", "ZZZZ"));
    }

    [Theory]
    // What the simulator reports, its ATC model, its title, what its package declares → the type named.
    [InlineData("B738", "", "FSLTL_B738_RYR-Ryanair", "A320", "737")]                  // the title's own designator beats a wrong declaration
    [InlineData("Panavia", "", "TORIAF2", "TOR", "Tornado")]                           // IVAO: the ATC type is the maker
    [InlineData("Sikorsky", "", "Simaddons Seaking Cougar", "S61", "Sea King")]
    [InlineData("ApronLife", "HELPER", "ApronLife_JetwayHelper_H AAL", "B772", "777")]
    [InlineData("PA-44", "", "PA44ALZ", "", "Piper Seminole")]                          // nothing declared: the reported type has a name
    [InlineData("", "", "A5", "", "Icon A5")]                                          // nothing but a title that is the designator
    [InlineData("Acme", "", "Acme Skyliner 900", "SKY9", "SKY9")]                      // a new type nobody named: its code, never its maker
    [InlineData("Acme", "", "Acme Skyliner 900", "see the manual", "Acme")]            // and no code declared: what the simulator reports
    [InlineData("", "", "Mystery", "", "")]
    public void The_naming_type_comes_from_the_title_then_the_package_then_the_simulator(
        string received, string atcModel, string title, string declared, string spoken)
        => Assert.Equal(spoken, GroundTrafficLogic.SpokenType(GroundTrafficLogic.NamingType(received, atcModel, title, declared)));

    // ── In a list ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("E55P", "Phenom 300")] [InlineData("GLEX", "Global Express")] [InlineData("C56X", "Citation Excel")]
    [InlineData("DH8D", "Dash 8")] [InlineData("AT76", "ATR 72")] [InlineData("E190", "Embraer 190")] [InlineData("C172", "Cessna 172")]
    [InlineData("B38M", "737 MAX")] [InlineData("Generic", "airliner")]
    // The designator already reads as what it is, and keeps the variant the family name drops.
    [InlineData("B738", "B738")] [InlineData("A20N", "A20N")] [InlineData("A359", "A359")] [InlineData("MD82", "MD82")]
    [InlineData("CRJ9", "CRJ9")] [InlineData("TBM9", "TBM9")] [InlineData("C130", "C130")] [InlineData("DC10", "DC10")]
    [InlineData("XY12", "XY12")] [InlineData("ZZZZ", "ZZZZ")] [InlineData("", "")]
    public void A_list_shows_the_name_of_an_opaque_designator_and_keeps_one_that_reads_as_itself(string raw, string listed)
        => Assert.Equal(listed, GroundTrafficLogic.ListedType(raw));

    [Fact]
    public void The_traffic_window_shows_the_airline_and_the_named_type()
    {
        var traffic = new MSFSBlindAssist.Models.TcasTraffic
        {
            ObjectId = 7, Callsign = "LXJ379", Airline = "Flexjet", AircraftType = "E55P", OnGround = false,
            DistanceNm = 4.2, AltitudeDiffFt = 2000, RelativeBearing = 60, GroundSpeedKnots = 310, AltitudeFt = 14500, HeadingMagnetic = 145,
        };
        string line = MSFSBlindAssist.Forms.TcasForm.BuildItemText(traffic, null);
        Assert.Contains(" — Flexjet — type Phenom 300 — ", line);
        Assert.StartsWith("LXJ 379 — ", line);
    }

    // ── The airline ──────────────────────────────────────────────────────────────────────────────

    private static string Known(string code) => code switch { "RYR" => "Ryanair", "BAW" => "Speedbird", _ => "" };

    [Theory]
    [InlineData("Speedbird", "RYR", "RYR123", "Speedbird")]   // the model's own airline always stands
    [InlineData("", "RYR", "N123AB", "Ryanair")]              // none on the model: the code its package declares
    [InlineData("", "", "RYR123", "Ryanair")]                 // else the code its callsign begins with
    [InlineData("", "", " ryr12a ", "Ryanair")]
    [InlineData("", "GA2J", "BAW45", "Speedbird")]            // AIG's made-up codes for private aircraft are no airline
    [InlineData("", "", "N123AB", "")]                        // a registration is not a flight
    [InlineData("", "", "GABCD", "")]
    [InlineData("", "", "RY123", "")]                         // two letters: an IATA code, not looked up
    [InlineData("", "", "RYRA12", "")]
    [InlineData("", "", "EZY123", "")]                        // a code no installed livery names
    [InlineData("", "", "", "")]
    public void The_airline_is_the_models_else_the_one_the_installed_liveries_name(string modelAirline, string declaredCode, string callsign, string airline)
        => Assert.Equal(airline, GroundTrafficLogic.SpokenAirline(modelAirline, declaredCode, callsign, Known));

    [Fact]
    public void With_no_catalog_the_airline_is_the_models_alone()
    {
        Assert.Equal("", GroundTrafficLogic.SpokenAirline("", "RYR", "RYR123", null));
        Assert.Equal("Delta", GroundTrafficLogic.SpokenAirline(" Delta ", "", "", null));
    }

    private static GroundTrafficModelCatalog.ModelRef Livery(string code, string airline) => new("dir", "", null, "A320", code, airline);

    [Fact]
    public void An_airlines_name_is_the_one_most_of_its_liveries_carry_in_its_best_spelling()
    {
        var names = GroundTrafficModelCatalog.AirlineNames(new[]
        {
            Livery("BAW", "SPEEDBIRD"), Livery("BAW", "SPEEDBIRD"), Livery("BAW", "Speedbird"),    // one name, two spellings
            Livery("BAW", "Shuttle"),                                                              // a subsidiary's
            Livery("KLM", "KLM"), Livery("KLM", "KLM"),
            Livery("GA2J", "Private"), Livery("", "Nobody"), Livery("RYR", ""), Livery("R2D", "Droid"),
        });

        Assert.Equal("Speedbird", names["BAW"]);
        Assert.Equal("Speedbird", names["baw"]);
        Assert.Equal("KLM", names["KLM"]);
        Assert.Equal(2, names.Count);          // no made-up code, no nameless livery, no code with a digit
    }

    // ── From the installed packages ──────────────────────────────────────────────────────────────

    private void WritePackage(string package, string folder, string cfg)
    {
        string dir = Path.Combine(_dir, "Community", package, "SimObjects", "Airplanes", folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "aircraft.cfg"), cfg);
    }

    private GroundTrafficModelCatalog InstalledCatalog()
    {
        // An IVAO-style model: its title is its type and its airline's code, its ATC type is the maker, and
        // it gives no airline. [GENERAL] comes after the liveries, as in most aircraft.cfg files.
        WritePackage("IVAO_MTL", "TOR", "[FLTSIM.0]\ntitle=TORRYR\nmodel=\n[GENERAL]\natc_type=Panavia\nicao_type_designator=\"tor\" ; a comment\n");
        // Liveries of a traffic library: the airline's code and its radio name.
        WritePackage("traffic", "A320", "[GENERAL]\nicao_type_designator=A320\n"
            + "[FLTSIM.0]\ntitle=Lib A320 Ryanair\natc_airline=\"Ryanair\"\nicao_airline=RYR\n"
            + "[FLTSIM.1]\ntitle=Lib A320 Private\nicao_airline=GA2J\n");
        return new GroundTrafficModelCatalog(null, () => _dir);
    }

    [Fact]
    public void The_catalog_knows_what_each_package_declares_once_the_packages_are_read()
    {
        using var catalog = InstalledCatalog();
        Assert.Equal(0, catalog.IndexVersion);
        Assert.Equal("", catalog.AirlineName("RYR"));

        // Asking what a title declares has the packages read in the background, with nothing measured:
        // the traffic window names airborne traffic, for which no outline is ever asked.
        Assert.Equal(("", ""), catalog.Declared("TORRYR"));
        Assert.True(SpinWait.SpinUntil(() => catalog.IndexVersion == 1, 10_000));
        Assert.False(catalog.IsSettled("TORRYR"));

        catalog.TryGet("TORRYR");
        Assert.True(SpinWait.SpinUntil(() => catalog.IsSettled("TORRYR"), 10_000));

        Assert.Equal(1, catalog.IndexVersion);
        Assert.Equal(("TOR", ""), catalog.Declared(" torryr "));
        Assert.Equal(("A320", "RYR"), catalog.Declared("Lib A320 Ryanair"));
        Assert.Equal(("A320", "GA2J"), catalog.Declared("Lib A320 Private"));
        Assert.Equal(("", ""), catalog.Declared("No such title"));
        Assert.Equal(("", ""), catalog.Declared(null));
        Assert.Equal("Ryanair", catalog.AirlineName("ryr"));
        Assert.Equal("", catalog.AirlineName("GA2J"));
        Assert.Equal("", catalog.AirlineName(""));
    }

    [Fact]
    public void A_model_that_gives_no_airline_and_a_maker_for_its_type_is_still_called_properly()
    {
        // RYR123 flown by the IVAO-style model: the simulator reports "Panavia" and no airline. Until the
        // packages are read it is named by what the simulator gave; from then on by what they declare,
        // with nothing new from the simulator: the name is rebuilt when the catalog's index arrives.
        using var catalog = InstalledCatalog();
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        h.UseRealSizes("A320", 117.5, catalog);
        var ahead = Ac(1, 550, 0, 0, "", "RYR123");      // inside the 2,000 ft the monitor tracks
        ahead.AircraftType = "Panavia";
        ahead.Title = "TORRYR";
        h.Sim.Traffic.Add(ahead);
        h.Sim.Position = Own(0, 0);
        h.Tick(6);                                      // the first sweep that reports it asks the catalog for its title
        Assert.True(SpinWait.SpinUntil(() => catalog.IsSettled("TORRYR"), 10_000), string.Join(" | ", h.Transcript));

        double east = 0;
        for (int s = 0; s < 200 && !h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal)); s++)
        {
            east += 10 * 0.514444;
            h.Sim.Position = Own(east, 10);
            h.Tick();
        }

        Assert.NotEmpty(h.Said.All);
        Assert.All(h.Said.All, m => Assert.Contains("Ryanair Tornado", m));
        Assert.DoesNotContain(h.Said.All, m => m.Contains("Panavia", StringComparison.Ordinal) || m.Contains("RYR", StringComparison.Ordinal));
    }

    [Fact]
    public void A_business_jet_and_a_light_aircraft_are_called_by_name()
    {
        // AIG's Phenom 300: the simulator's ATC type resolves to E55P, its airline is the radio name; and a
        // private Cessna with no airline, named by its registration.
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        var jet = Ac(1, 250, 0, 0, "FLEXJET", "N379FX");
        jet.AircraftType = "E55P";
        jet.Title = "AIGAIM_Flexjet Embraer Phenom 300E - N379FX";
        var cessna = Ac(2, 400, 60, 0, "", "N172SP");
        cessna.AircraftType = "C172";
        cessna.Title = "AIGAIM_Generic Cessna 172 - WP09";
        h.Sim.Traffic.Add(jet);
        h.Sim.Traffic.Add(cessna);
        h.Sim.Position = Own(0, 6);
        h.Tick(4);

        Assert.Contains(h.Said.All, m => m.Contains("FLEXJET Phenom 300", StringComparison.Ordinal));
        Assert.Contains("Flexjet Phenom 300", string.Join(" | ", h.Said.All), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(h.Said.All, m => m.Contains("E55P", StringComparison.Ordinal));
    }
}
