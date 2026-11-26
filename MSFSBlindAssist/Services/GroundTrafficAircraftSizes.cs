using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>Where an <see cref="AircraftSize"/> came from, most trusted first.</summary>
public enum AircraftSizeSource
{
    /// <summary>The type was recognised: published dimensions from the table.</summary>
    Table,
    /// <summary>Type unknown: scaled from the wing span the simulator reports for the model.</summary>
    WingSpan,
    /// <summary>Nothing known: the widebody the monitor's distances always assumed.</summary>
    Default,
}

/// <summary>An aircraft's overall length and wing span in metres, and how they were found.</summary>
public readonly record struct AircraftSize(double LengthM, double WingspanM, AircraftSizeSource Source, string Icao);

/// <summary>
/// How big a traffic aircraft is, for the ground-traffic monitor's nose/tail/wingtip distances
/// (<see cref="AircraftHull"/>). The simulator has no length variable at all and its <c>WING SPAN</c>
/// is whatever the model's flight model says — measured live at OMDB on 2026-11-26: 61.87 m for every
/// AIG 777, and 15.48 m for a SayIntentions 777 and for most IVAO models — so size comes from the TYPE,
/// looked up in a table of published dimensions.
/// <para>The type is read from what a sweep delivers, most specific first: the model's <c>TITLE</c>
/// (FSLTL "FSLTL_FAIB_A20N_Aircalin", IVAO and SayIntentions "B773UAE", AIG "AIGAIM_Emirates Boeing
/// 777-300ER - Fwd Dome"), then the ATC type and model. AIG's own ATC model is only a family
/// ("$$:Triple 7", "$$:737" for a MAX 8 and a BBJ alike), which is why the title comes first.</para>
/// Pure: pinned by GroundTrafficAircraftSizesTests.
/// </summary>
public static class GroundTrafficAircraftSizes
{
    /// <summary>
    /// The size assumed when nothing is known: a 61 m aircraft, the widebody the monitor's zone distances
    /// were sized for before it knew sizes (two 787s, about 200 ft of combined half-fuselage). Erring
    /// large is the safe side: a gap spoken too small, a "Stop" too early.
    /// </summary>
    public static readonly AircraftSize Default = new(60.96, 60.96, AircraftSizeSource.Default, "");

    /// <summary>An unknown type's length as a multiple of the wing span the simulator reports (airliners run 1.0 to 1.5).</summary>
    public const double LengthPerWingspan = 1.2;

    /// <summary>A reported wing span outside this range (metres) is not believed.</summary>
    public const double MinPlausibleWingspanM = 4.0, MaxPlausibleWingspanM = 90.0;

    // ICAO type designator → (overall length, wing span) in metres, from the manufacturers' published
    // dimensions. Winglet and sharklet variants take the wider span. Helicopters: fuselage length and
    // rotor diameter. Folding wingtips (777X) are listed folded, as they are on the ground.
    private static readonly Dictionary<string, (double Length, double Span)> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Airbus
        ["A318"] = (31.44, 34.10), ["A319"] = (33.84, 35.80), ["A320"] = (37.57, 35.80), ["A321"] = (44.51, 35.80),
        ["A19N"] = (33.84, 35.80), ["A20N"] = (37.57, 35.80), ["A21N"] = (44.51, 35.80),
        ["A306"] = (54.08, 44.84), ["A30B"] = (53.62, 44.84), ["A310"] = (46.66, 43.90), ["A3ST"] = (56.15, 44.84),
        ["A332"] = (58.82, 60.30), ["A333"] = (63.67, 60.30), ["A337"] = (63.10, 60.30),
        ["A338"] = (58.82, 64.00), ["A339"] = (63.66, 64.00),
        ["A342"] = (59.40, 60.30), ["A343"] = (63.69, 60.30), ["A345"] = (67.93, 63.45), ["A346"] = (75.36, 63.45),
        ["A359"] = (66.80, 64.75), ["A35K"] = (73.79, 64.75), ["A388"] = (72.72, 79.75),
        ["A400"] = (45.10, 42.40), ["BCS1"] = (35.00, 35.10), ["BCS3"] = (38.71, 35.10),
        // Boeing
        ["B703"] = (46.61, 44.42), ["B712"] = (37.81, 28.45), ["B721"] = (40.59, 32.92), ["B722"] = (46.69, 32.92),
        ["B732"] = (30.53, 28.35), ["B733"] = (33.40, 28.88), ["B734"] = (36.45, 28.88), ["B735"] = (31.01, 28.88),
        ["B736"] = (31.24, 34.32), ["B737"] = (33.63, 35.79), ["B738"] = (39.47, 35.79), ["B739"] = (42.11, 35.79),
        ["B37M"] = (35.56, 35.92), ["B38M"] = (39.52, 35.92), ["B39M"] = (42.16, 35.92), ["B3XM"] = (43.80, 35.92),
        ["B741"] = (70.66, 59.64), ["B742"] = (70.66, 59.64), ["B743"] = (70.66, 59.64), ["B744"] = (70.66, 64.44),
        ["B748"] = (76.25, 68.40), ["B74S"] = (56.31, 59.64), ["B74F"] = (70.66, 64.44), ["BLCF"] = (71.68, 64.44),
        ["B752"] = (47.32, 38.05), ["B753"] = (54.43, 38.05),
        ["B762"] = (48.51, 47.57), ["B763"] = (54.94, 50.88), ["B764"] = (61.37, 51.92),
        ["B772"] = (63.73, 60.93), ["B77L"] = (63.73, 64.80), ["B773"] = (73.86, 60.93), ["B77W"] = (73.86, 64.80),
        ["B778"] = (70.87, 64.85), ["B779"] = (76.72, 64.85),
        ["B788"] = (56.72, 60.12), ["B789"] = (62.81, 60.12), ["B78X"] = (68.28, 60.12),
        ["P8"] = (39.47, 37.64), ["E737"] = (33.63, 35.79), ["E3"] = (46.61, 44.42), ["K35R"] = (41.53, 39.88),
        ["C17"] = (53.00, 51.75),
        // McDonnell Douglas, Douglas, Lockheed
        ["MD11"] = (61.62, 51.97), ["DC10"] = (55.35, 50.39), ["MD81"] = (45.06, 32.87), ["MD82"] = (45.06, 32.87),
        ["MD83"] = (45.06, 32.87), ["MD87"] = (39.75, 32.87), ["MD88"] = (45.06, 32.87), ["MD8X"] = (45.06, 32.87),
        ["MD90"] = (46.51, 32.87), ["DC91"] = (31.82, 27.25), ["DC93"] = (36.36, 28.47), ["DC3"] = (19.66, 28.96),
        ["DC6"] = (32.18, 35.81), ["L101"] = (54.17, 47.35), ["C130"] = (29.79, 40.41), ["C5"] = (75.31, 67.89),
        ["CONI"] = (35.41, 38.47),
        // Antonov, Ilyushin, Tupolev, Sukhoi, Comac
        ["A124"] = (68.96, 73.30), ["A225"] = (84.00, 88.40), ["AN12"] = (33.10, 38.00), ["AN22"] = (57.90, 64.40),
        ["AN24"] = (23.53, 29.20), ["AN2"] = (12.40, 18.20), ["IL76"] = (46.59, 50.50), ["IL78"] = (46.59, 50.50),
        ["A50"] = (46.59, 50.50), ["IL96"] = (55.35, 60.11), ["IL38"] = (39.60, 37.42), ["T134"] = (37.10, 29.00),
        ["T154"] = (47.90, 37.55), ["T144"] = (65.70, 28.80), ["T160"] = (54.10, 55.70), ["SU95"] = (29.94, 27.80),
        ["AJ27"] = (33.46, 27.29), ["C919"] = (38.90, 35.80),
        // Embraer
        ["E110"] = (15.10, 15.33), ["E120"] = (20.00, 19.78), ["E135"] = (26.33, 20.04), ["E145"] = (29.87, 20.04),
        ["E45X"] = (29.87, 21.00), ["E35L"] = (26.33, 21.17), ["E170"] = (29.90, 26.00), ["E175"] = (31.68, 28.65),
        ["E75S"] = (31.68, 26.00), ["E75L"] = (31.68, 28.65), ["E190"] = (36.24, 28.72), ["E195"] = (38.65, 28.72),
        ["E290"] = (36.25, 33.72), ["E295"] = (41.50, 35.12), ["E50P"] = (12.82, 12.30), ["E55P"] = (15.64, 15.91),
        ["E545"] = (20.74, 21.50), ["E550"] = (20.74, 21.50),
        // Bombardier, De Havilland Canada
        ["CRJ1"] = (26.77, 21.21), ["CRJ2"] = (26.77, 21.21), ["CRJ7"] = (32.51, 23.24), ["CRJ9"] = (36.40, 24.85),
        ["CRJX"] = (39.13, 26.18), ["CL30"] = (20.92, 19.46), ["CL35"] = (20.92, 21.00), ["CL60"] = (20.85, 19.61),
        ["CL2T"] = (19.82, 28.60), ["GLEX"] = (30.30, 28.65), ["GL5T"] = (29.50, 28.65), ["GL7T"] = (33.80, 31.70),
        ["DH8A"] = (22.25, 25.91), ["DH8B"] = (22.25, 25.91), ["DH8C"] = (25.68, 27.43), ["DH8D"] = (32.83, 28.42),
        ["DHC2"] = (9.22, 14.63), ["DHC6"] = (15.77, 19.81), ["DHC7"] = (24.58, 28.35),
        // ATR, Fokker, BAe, Saab, Dornier, Shorts
        ["AT43"] = (22.67, 24.57), ["AT44"] = (22.67, 24.57), ["AT45"] = (22.67, 24.57), ["AT46"] = (22.67, 24.57),
        ["AT72"] = (27.17, 27.05), ["AT73"] = (27.17, 27.05), ["AT75"] = (27.17, 27.05), ["AT76"] = (27.17, 27.05),
        ["F28"] = (29.61, 25.07), ["F50"] = (25.25, 29.00), ["F70"] = (30.91, 28.08), ["F100"] = (35.53, 28.08),
        ["B461"] = (26.19, 26.34), ["B462"] = (28.60, 26.34), ["B463"] = (30.99, 26.34),
        ["RJ70"] = (26.19, 26.34), ["RJ85"] = (28.60, 26.34), ["RJ1H"] = (30.99, 26.34),
        ["JS31"] = (14.37, 15.85), ["JS32"] = (14.37, 15.85), ["JS41"] = (19.25, 18.29),
        ["SF34"] = (19.73, 21.44), ["SB20"] = (27.28, 24.76), ["SH36"] = (21.58, 22.80),
        ["D328"] = (21.28, 20.98), ["J328"] = (21.28, 20.98), ["C160"] = (32.40, 40.00), ["C295"] = (24.50, 25.81),
        ["CN35"] = (21.40, 25.81),
        // Business jets
        ["LJ25"] = (14.50, 10.84), ["LJ31"] = (14.83, 13.36), ["LJ35"] = (14.83, 12.04), ["LJ40"] = (16.93, 14.56),
        ["LJ45"] = (17.68, 14.56), ["LJ55"] = (16.79, 13.34), ["LJ60"] = (17.88, 13.34), ["LJ70"] = (17.08, 15.51),
        ["LJ75"] = (17.56, 15.51), ["H25B"] = (15.60, 15.66), ["WW24"] = (15.93, 13.65), ["BE40"] = (14.76, 13.25),
        ["PRM1"] = (14.02, 13.56), ["HDJT"] = (12.99, 12.12),
        ["GLF3"] = (25.32, 23.72), ["GLF4"] = (26.92, 23.72), ["GLF5"] = (29.39, 28.50), ["GLF6"] = (30.41, 30.36),
        ["G280"] = (20.30, 19.20),
        ["FA10"] = (13.86, 13.08), ["FA20"] = (17.15, 16.30), ["FA50"] = (18.52, 18.86), ["F900"] = (20.21, 21.38),
        ["F2TH"] = (20.23, 21.38), ["FA7X"] = (23.38, 26.21), ["FA8X"] = (24.46, 26.29),
        ["C510"] = (12.37, 13.16), ["C525"] = (12.98, 14.30), ["C25A"] = (14.53, 15.19), ["C25B"] = (15.59, 16.26),
        ["C25C"] = (16.26, 15.49), ["C550"] = (14.39, 15.90), ["C560"] = (16.00, 17.17), ["C56X"] = (16.00, 17.17),
        ["C680"] = (19.35, 22.04), ["C68A"] = (19.02, 22.04), ["C700"] = (22.30, 20.98), ["C750"] = (22.43, 21.10),
        ["PC24"] = (16.85, 17.00), ["SF50"] = (9.42, 11.79),
        // Turboprops and piston twins
        ["B190"] = (17.63, 17.67), ["B350"] = (14.22, 17.65), ["BE20"] = (13.36, 16.61), ["BE9L"] = (10.82, 15.32),
        ["BE99"] = (13.58, 13.98), ["BE10"] = (12.17, 13.98), ["BE12"] = (10.82, 13.98), ["BE58"] = (9.09, 11.53),
        ["BE60"] = (10.31, 11.96), ["C208"] = (12.67, 15.87), ["C402"] = (11.09, 13.45), ["C404"] = (12.04, 14.12),
        ["C408"] = (16.71, 22.02), ["C414"] = (11.09, 13.45), ["C421"] = (11.09, 12.53), ["C441"] = (11.89, 15.04),
        ["F406"] = (11.89, 15.08), ["PC12"] = (14.40, 16.28), ["PC6"] = (10.90, 15.87), ["TBM7"] = (10.65, 12.68),
        ["TBM8"] = (10.65, 12.68), ["TBM9"] = (10.74, 12.83), ["KODI"] = (10.42, 13.72), ["EPIC"] = (10.92, 13.10),
        ["MU2"] = (12.01, 11.94), ["SW3"] = (12.85, 14.10), ["SW4"] = (18.09, 17.37), ["BN2P"] = (10.86, 14.94),
        ["P180"] = (14.41, 14.03), ["PA31"] = (9.94, 12.40), ["PA46"] = (8.81, 13.11), ["AEST"] = (10.61, 11.18),
        ["C337"] = (9.07, 11.63), ["DA42"] = (8.56, 13.55), ["DA62"] = (9.19, 14.55), ["JU52"] = (18.90, 29.25),
        // Light singles
        ["C140"] = (6.55, 10.16), ["C152"] = (7.34, 10.16), ["C170"] = (7.61, 10.97), ["C172"] = (8.28, 11.00),
        ["C72R"] = (8.28, 11.00), ["C177"] = (8.28, 10.82), ["C77R"] = (8.28, 10.82), ["C182"] = (8.84, 10.97),
        ["C82R"] = (8.84, 10.97), ["C185"] = (7.85, 10.92), ["C206"] = (8.61, 10.97), ["C207"] = (9.68, 10.97),
        ["C210"] = (8.59, 11.20), ["P210"] = (8.59, 11.20), ["P28A"] = (7.25, 10.67), ["P28R"] = (7.52, 10.80),
        ["P28S"] = (7.52, 10.80), ["P28U"] = (7.52, 10.80), ["PA24"] = (7.59, 10.97), ["J3"] = (6.83, 10.74),
        ["DA40"] = (8.06, 11.94), ["DR40"] = (6.96, 8.72), ["SR20"] = (7.92, 11.68), ["SR22"] = (7.92, 11.68),
        ["BE35"] = (8.13, 10.21), ["BE36"] = (8.38, 10.21), ["AA5"] = (6.71, 9.60), ["M20P"] = (8.15, 11.00),
        ["M20T"] = (8.15, 11.00), ["BRAV"] = (8.15, 11.00), ["G115"] = (7.79, 10.00), ["VL3"] = (6.24, 8.44),
        ["F260"] = (7.10, 8.35), ["S205"] = (8.00, 10.86), ["S208"] = (8.00, 10.86), ["SPIT"] = (9.12, 11.23),
        ["SV4"] = (6.80, 8.38),
        // Military jets and trainers
        ["F16"] = (15.03, 9.96), ["F22"] = (18.90, 13.56), ["F35"] = (15.70, 10.70), ["F35A"] = (15.70, 10.70),
        ["F35B"] = (15.60, 10.70), ["F35C"] = (15.70, 13.10), ["F18H"] = (17.07, 11.43), ["F18S"] = (18.31, 13.62),
        ["FA18E"] = (18.31, 13.62), ["F14"] = (19.10, 19.55), ["F4"] = (19.20, 11.77), ["F117"] = (20.09, 13.20),
        ["A4"] = (12.22, 8.38), ["MG29"] = (17.32, 11.36), ["MIR2"] = (14.36, 9.13), ["EUFI"] = (15.96, 10.95),
        ["RFAL"] = (15.27, 10.90), ["TOR"] = (16.72, 13.91), ["HAR"] = (14.12, 9.25), ["KFIR"] = (15.65, 8.22),
        ["ETAR"] = (14.31, 9.60), ["AJET"] = (11.75, 9.11), ["L39"] = (12.13, 9.46), ["T38"] = (14.14, 7.70),
        ["TEX2"] = (10.16, 10.19), ["M339"] = (10.97, 11.22), ["PC21"] = (11.23, 9.11), ["FOUGA"] = (10.06, 12.15),
        ["SR71"] = (32.74, 16.94), ["U2"] = (19.20, 31.39), ["B1"] = (44.50, 41.80), ["B2"] = (21.00, 52.40),
        ["VULC"] = (30.45, 33.83), ["BUC"] = (19.33, 13.41), ["E2"] = (17.60, 24.56), ["CONC"] = (61.66, 25.60),
        // Helicopters (fuselage length, rotor diameter)
        ["A109"] = (11.45, 11.00), ["AS50"] = (10.93, 10.69), ["AS32"] = (16.79, 16.20), ["AS65"] = (11.63, 11.94),
        ["B06"] = (9.50, 10.16), ["B407"] = (10.57, 10.67), ["B214"] = (14.63, 15.24), ["B222"] = (12.85, 12.80),
        ["EC35"] = (10.20, 10.20), ["EC45"] = (10.19, 11.00), ["EC25"] = (16.79, 16.20), ["EC75"] = (12.11, 12.60),
        ["H160"] = (13.96, 13.40), ["H60"] = (15.26, 16.36), ["H53"] = (22.35, 24.08), ["H64"] = (14.68, 14.63),
        ["CH47"] = (15.54, 18.29), ["R22"] = (6.30, 7.67), ["R44"] = (9.07, 10.06), ["MD52"] = (7.77, 8.33),
        ["MI8"] = (18.17, 21.29), ["UH1"] = (12.77, 14.63), ["S64"] = (21.41, 21.95), ["V22"] = (17.48, 25.78),
        ["G2CA"] = (6.31, 7.20),
    };

    // A model TITLE or ATC model that names the aircraft in words rather than by designator, in the
    // order tried: the first pattern that matches gives the type. Specific variants come before their
    // family, and each family's catch-all is its LARGEST common variant (the safe side). Written against
    // every AIG model title installed on 2026-11-26 (342 models).
    private static readonly (Regex Pattern, string Icao)[] Names = BuildNames(
        // Airbus
        (@"A220[\s-]?100|CS100", "BCS1"), (@"A220|CS300", "BCS3"),
        (@"A300[\s-]?600ST|BELUGA", "A3ST"), (@"A300B", "A30B"), (@"A300", "A306"), (@"A310", "A310"),
        (@"A318|ACJ318", "A318"), (@"A319[\s-]?(100)?NEO|ACJ319NEO", "A19N"), (@"A319|ACJ319", "A319"),
        (@"A321[\s-]?(200)?\s?NEO", "A21N"), (@"A321", "A321"),
        (@"A320[\s-]?(200)?NEO|ACJ320NEO", "A20N"), (@"A320|ACJ320", "A320"),
        (@"A330[\s-]?800", "A338"), (@"A330[\s-]?900", "A339"), (@"A330[\s-]?200|A330\s?MRTT|KC[\s-]?30", "A332"),
        (@"A330", "A333"),
        (@"A340[\s-]?200", "A342"), (@"A340[\s-]?500", "A345"), (@"A340[\s-]?600", "A346"), (@"A340", "A343"),
        (@"A350[\s-]?1000", "A35K"), (@"A350", "A359"), (@"A380", "A388"), (@"A400M", "A400"),
        // Boeing
        (@"\b717", "B712"), (@"\b727[\s-]?100", "B721"), (@"\b727", "B722"),
        (@"737[\s-]?MAX[\s-]?7", "B37M"), (@"737[\s-]?MAX[\s-]?9", "B39M"), (@"737[\s-]?MAX[\s-]?10", "B3XM"),
        (@"737[\s-]?MAX", "B38M"),
        (@"737[\s-]?200", "B732"), (@"737[\s-]?300", "B733"), (@"737[\s-]?400", "B734"), (@"737[\s-]?500", "B735"),
        (@"737[\s-]?600", "B736"), (@"737[\s-]?700|BBJ1?\b|\bC-40", "B737"), (@"737[\s-]?900|BBJ3", "B739"),
        (@"\b737|BBJ2|P[\s-]?8A", "B738"),
        (@"747[\s-]?400\s?LCF|DREAMLIFTER", "BLCF"), (@"747[\s-]?8", "B748"), (@"747[\s-]?SP", "B74S"),
        (@"747[\s-]?100", "B741"), (@"747[\s-]?200", "B742"), (@"747[\s-]?300", "B743"), (@"\b747", "B744"),
        (@"757[\s-]?300", "B753"), (@"\b757|\bC-32", "B752"),
        (@"767[\s-]?200", "B762"), (@"767[\s-]?400", "B764"), (@"\b767", "B763"),
        (@"777[\s-]?200\s?(LR|F)\b", "B77L"), (@"777[\s-]?200", "B772"), (@"777[\s-]?300\s?ER", "B77W"),
        (@"777[\s-]?300", "B773"), (@"777[\s-]?8", "B778"), (@"777[\s-]?9", "B779"), (@"\b777|TRIPLE\s?7", "B77W"),
        (@"787[\s-]?8", "B788"), (@"787[\s-]?10", "B78X"), (@"\b787|DREAMLINER", "B789"),
        // McDonnell Douglas, Douglas, Lockheed
        (@"MD[\s-]?11", "MD11"), (@"DC[\s-]?10", "DC10"), (@"MD[\s-]?87", "MD87"), (@"MD[\s-]?90", "MD90"),
        (@"MD[\s-]?8\d", "MD82"), (@"DC[\s-]?9[\s-]?1\d", "DC91"), (@"DC[\s-]?9", "DC93"),
        (@"C[\s-]?130|HERCULES", "C130"), (@"AN[\s-]?124", "A124"), (@"AN[\s-]?225", "A225"),
        (@"SUPERJET|SSJ", "SU95"),
        // Embraer
        (@"(ERJ|EMB)[\s-]?145\s?XR", "E45X"), (@"(ERJ|EMB)[\s-]?14[05]", "E145"), (@"(ERJ|EMB)[\s-]?135", "E135"),
        (@"LEGACY\s?6\d\d", "E35L"), (@"(EMB|E|ERJ)[\s-]?195[\s-]?E2", "E295"), (@"(EMB|E|ERJ)[\s-]?190[\s-]?E2", "E290"),
        (@"(EMB|ERJ|EMBRAER)[\s-]?195", "E195"), (@"(EMB|ERJ|EMBRAER)[\s-]?190", "E190"),
        (@"(EMB|ERJ|EMBRAER)[\s-]?175", "E75L"), (@"(EMB|ERJ|EMBRAER)[\s-]?170", "E170"),
        (@"EMB[\s-]?120|BRASILIA", "E120"), (@"EMB[\s-]?110|BANDEIRANTE", "E110"),
        (@"PHENOM\s?300", "E55P"), (@"PHENOM\s?100", "E50P"),
        // Bombardier, De Havilland Canada
        (@"CRJ[\s-]?1000", "CRJX"), (@"CRJ[\s-]?900", "CRJ9"), (@"CRJ[\s-]?(700|550)", "CRJ7"),
        (@"CRJ[\s-]?[12]00|CHALLENGER\s?850", "CRJ2"), (@"CRJ", "CRJ9"),
        (@"CHALLENGER\s?350", "CL35"), (@"CHALLENGER\s?300", "CL30"), (@"CHALLENGER", "CL60"),
        (@"GLOBAL\s?5\d00", "GL5T"), (@"GLOBAL\s?7\d00", "GL7T"), (@"GLOBAL", "GLEX"),
        (@"DASH\s?8[\s-]?Q?400|\bQ400", "DH8D"), (@"DASH\s?8[\s-]?Q?300", "DH8C"), (@"DASH\s?8[\s-]?Q?[12]00", "DH8A"),
        (@"DASH\s?8", "DH8D"), (@"TWIN\s?OTTER|DHC[\s-]?6", "DHC6"),
        // ATR, Fokker, BAe, Saab, Dornier, Shorts
        (@"ATR[\s-]?42", "AT45"), (@"ATR[\s-]?72|\bATR\b", "AT76"),
        (@"FOKKER\s?100|F[\s-]?100\b", "F100"), (@"FOKKER\s?70", "F70"), (@"FOKKER\s?50", "F50"),
        (@"JETSTREAM\s?41", "JS41"), (@"JETSTREAM", "JS32"), (@"SAAB\s?2000", "SB20"), (@"SAAB\s?340", "SF34"),
        (@"328[\s-]?300|328\s?JET", "J328"), (@"DORNIER\s?328", "D328"), (@"SHORTS?\s?3[\s-]?60", "SH36"),
        // Business jets
        (@"LEARJET\s?31", "LJ31"), (@"LEARJET\s?3[56]", "LJ35"), (@"LEARJET\s?40", "LJ40"), (@"LEARJET\s?45", "LJ45"),
        (@"LEARJET\s?55", "LJ55"), (@"LEARJET\s?60", "LJ60"), (@"LEARJET\s?70", "LJ70"), (@"LEARJET\s?75", "LJ75"),
        (@"HAWKER\s?400|BEECHJET|NEXTANT", "BE40"), (@"HAWKER", "H25B"), (@"WESTWIND", "WW24"), (@"PREMIER", "PRM1"),
        (@"\bG650|GULFSTREAM\s?VI\b", "GLF6"), (@"\bG5[05]0|GULFSTREAM\s?V\b|\bG5\b|\bC-37", "GLF5"),
        (@"GULFSTREAM|\bG[34][05]0|\bG4\b|\bC-20[A-H]?\b", "GLF4"),
        (@"FALCON\s?10\b", "FA10"), (@"FALCON\s?20", "FA20"), (@"FALCON\s?50", "FA50"), (@"FALCON\s?900", "F900"),
        (@"FALCON\s?2000", "F2TH"), (@"FALCON\s?7X|\bF7X", "FA7X"), (@"FALCON\s?8X|\bF8X", "FA8X"),
        (@"CITATION\s?(JET\s?)?CJ3|525B", "C25B"), (@"CITATION\s?(JET\s?)?CJ4|525C", "C25C"),
        (@"CITATION\s?(JET\s?)?CJ2|525A", "C25A"), (@"CITATION\s?(XLS?|EXCEL)|560XL", "C56X"),
        (@"CITATION\s?LONGITUDE", "C700"), (@"CITATION\s?X\b", "C750"), (@"CITATION\s?MUSTANG", "C510"),
        (@"CITATION", "C560"),
        // Turboprops, twins and singles
        (@"1900[CD]?\b", "B190"), (@"KING\s?AIR\s?3[56]0", "B350"), (@"KING\s?AIR\s?[A-F]?90", "BE9L"),
        (@"KING\s?AIR\s?A?100", "BE10"), (@"KING\s?AIR", "BE20"), (@"C99\b", "BE99"), (@"BARON", "BE58"),
        (@"BONANZA", "BE36"), (@"PC[\s-]?12", "PC12"), (@"PC[\s-]?24", "PC24"),
        (@"METRO|MERLIN\s?IV", "SW4"), (@"MERLIN", "SW3"), (@"ISLANDER|BN[\s-]?2", "BN2P"),
        (@"F406|CARAVAN\s?II\b", "F406"), (@"CARAVAN|CARGOMASTER|CESSNA\s?208", "C208"),
        (@"CESSNA\s?(R|T)?140", "C140"), (@"CESSNA\s?152", "C152"), (@"CESSNA\s?170", "C170"),
        (@"CESSNA\s?(R|T)?172|SKYHAWK|CUTLASS", "C172"), (@"CESSNA\s?177|CARDINAL", "C177"),
        (@"CESSNA\s?(R|T)?182|SKYLANE", "C182"), (@"CESSNA\s?185|SKYWAGON", "C185"),
        (@"CESSNA\s?(T|U)?206|STATIONAIR", "C206"), (@"CESSNA\s?P?210|CENTURION", "C210"),
        (@"CESSNA\s?402", "C402"), (@"CESSNA\s?404", "C404"), (@"CESSNA\s?414", "C414"), (@"CESSNA\s?421", "C421"),
        (@"CESSNA\s?441|CONQUEST", "C441"),
        (@"PA[\s-]?28R|ARROW", "P28R"), (@"PA[\s-]?28|ARCHER|WARRIOR|CHEROKEE", "P28A"),
        (@"PA[\s-]?31|NAVAJO|CHIEFTAIN", "PA31"), (@"PA[\s-]?46|MALIBU", "PA46"),
        (@"SR[\s-]?2[02]|CIRRUS", "SR22"), (@"DR[\s-]?400", "DR40"), (@"DA[\s-]?40", "DA40"), (@"DA[\s-]?42", "DA42"), (@"DA[\s-]?62", "DA62"),
        (@"TBM", "TBM9"));

    private static (Regex, string)[] BuildNames(params (string Pattern, string Icao)[] names)
        => names.Select(n => (new Regex(n.Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant), n.Icao)).ToArray();

    private static readonly Regex RxToken = new(@"[A-Z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // "TT:ATCCOM.AC_MODEL A320.0.text" → "A320": the sim's localisation key around an ATC type or model.
    private static readonly Regex RxLocKey = new(@"^(?:TT:)?ATCCOM\.(?:ATC_NAME|AC_MODEL)[ _](.+?)\.0\.TEXT$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly ConcurrentDictionary<(string, string, string), string> IcaoCache = new();

    /// <summary>Every type designator the size table holds (tests: each one has a spoken name).</summary>
    internal static IReadOnlyCollection<string> SizedTypes => Table.Keys;

    /// <summary>The published length and wing span of an ICAO type designator, or null when it is not in the table.</summary>
    public static (double LengthM, double WingspanM)? Lookup(string? icao)
        => icao != null && Table.TryGetValue(icao.Trim(), out var s) ? s : null;

    /// <summary>
    /// The ICAO type designator of a traffic aircraft, from its model <paramref name="title"/>, then its
    /// ATC <paramref name="type"/> and <paramref name="model"/> (raw, or already shortened by
    /// <c>ResolveAiAircraftType</c>); "" when none of them names a type the table knows.
    /// </summary>
    public static string ResolveIcao(string? type, string? model, string? title)
    {
        var key = (type ?? "", model ?? "", title ?? "");
        if (IcaoCache.TryGetValue(key, out string? known)) return known;
        // Count takes every lock of the cache: only when a key is about to be added.
        if (IcaoCache.Count > 4096) IcaoCache.Clear();
        return IcaoCache.GetOrAdd(key, static k => ResolveIcaoUncached(k.Item1, k.Item2, k.Item3));
    }

    private static string ResolveIcaoUncached(string type, string model, string title)
    {
        string t = Clean(title), ty = Clean(type), mo = Clean(model);
        // 1. A designator written out: a word of the title (FSLTL's ATC model says "A320" for its A20N), or
        //    the whole ATC type or model.
        if (DesignatorIn(t, isTitle: true) is { } fromTitle) return fromTitle;
        if (Table.ContainsKey(ty)) return ty;
        if (Table.ContainsKey(mo)) return mo;
        // 2. The aircraft named in words: the title (it carries the variant), then the ATC model, then the type.
        foreach (string s in new[] { WithoutNotes(t), mo, ty })
            if (NamedIn(s) is { } named) return named;
        // 3. A designator inside the ATC model or type ("A320 NEO CFML", "B78X GE").
        return DesignatorIn(mo, isTitle: false) ?? DesignatorIn(ty, isTitle: false) ?? "";
    }

    // AIG titles end in livery notes after " - " ("... Beechcraft 1900D - Gulfstream Intl N231YV",
    // "... Embraer ERJ-145 - C9-BBJ"): never part of the type.
    private static string WithoutNotes(string title)
    {
        int notes = title.IndexOf(" - ", StringComparison.Ordinal);
        return notes > 0 ? title[..notes] : title;
    }

    // The type a string names in words. The airline comes BEFORE the type in a title and may itself hold
    // a type word ("Global Jet International Hawker 800XP", "Baron Aviation Cessna 208B"), so the match
    // that starts LAST wins; between patterns that start together, the first listed (the more specific).
    private static string? NamedIn(string s)
    {
        if (s.Length == 0) return null;
        string? best = null;
        int bestAt = -1;
        foreach (var (pattern, icao) in Names)
        {
            int at = -1;
            foreach (Match m in pattern.Matches(s)) at = m.Index;
            if (at > bestAt) { bestAt = at; best = icao; }
        }
        return best;
    }

    /// <summary>
    /// A traffic aircraft's size: its type's published dimensions when the type is recognised
    /// (<see cref="ResolveIcao"/>); else scaled from the wing span the simulator reports
    /// (<paramref name="simWingspanM"/>, believed only inside the plausible range); else <see cref="Default"/>.
    /// </summary>
    public static AircraftSize Resolve(string? type, string? model, string? title, double simWingspanM)
    {
        string icao = ResolveIcao(type, model, title);
        if (icao.Length > 0 && Table.TryGetValue(icao, out var s))
            return new AircraftSize(s.Length, s.Span, AircraftSizeSource.Table, icao.ToUpperInvariant());
        if (double.IsFinite(simWingspanM) && simWingspanM >= MinPlausibleWingspanM && simWingspanM <= MaxPlausibleWingspanM)
            return new AircraftSize(simWingspanM * LengthPerWingspan, simWingspanM, AircraftSizeSource.WingSpan, "");
        return Default;
    }

    /// <summary>
    /// The pilot's OWN aircraft: the length of its type, and the wing span the simulator reports for it
    /// when that is plausible — an add-on the pilot flies has a real flight model, unlike traffic.
    /// </summary>
    public static AircraftSize ResolveOwn(string? icaoType, string? title, double simWingspanM)
    {
        var size = Resolve(icaoType, null, title, simWingspanM);
        bool spanOk = double.IsFinite(simWingspanM) && simWingspanM >= MinPlausibleWingspanM && simWingspanM <= MaxPlausibleWingspanM;
        return size.Source == AircraftSizeSource.Table && spanOk ? size with { WingspanM = simWingspanM } : size;
    }

    /// <summary>A measured outline further than this from its type's published length or span is not believed.</summary>
    public const double MaxMeasuredDeviation = 0.15;

    /// <summary>
    /// Does a measured outline fit the aircraft it is for? When the type is known (<paramref name="size"/>
    /// from the table), its length and span must be within <see cref="MaxMeasuredDeviation"/> of the
    /// published ones: a model whose parts could not all be placed (the Fenix's wings, 2026-11-26) measures
    /// as something else, and the published size is then the safer answer. An unknown type has nothing to
    /// hold the measurement against, so it stands.
    /// </summary>
    public static bool Agrees(AircraftOutline outline, AircraftSize size)
    {
        if (size.Source != AircraftSizeSource.Table) return true;
        return Math.Abs(outline.LengthM - size.LengthM) <= MaxMeasuredDeviation * size.LengthM
               && Math.Abs(outline.HalfSpanM * 2.0 - size.WingspanM) <= MaxMeasuredDeviation * size.WingspanM;
    }

    // Upper-cased, without AIG's "$$:" marker or the sim's localisation key.
    private static string Clean(string s)
    {
        s = s.Trim().ToUpperInvariant();
        if (s.StartsWith("$$:", StringComparison.Ordinal)) s = s[3..].Trim();
        var m = RxLocKey.Match(s);
        return m.Success ? m.Groups[1].Value.Trim() : s;
    }

    // The first word that is a designator. Only one holding a digit counts: a designator of letters alone
    // (AJET, GLEX, TOR) is too easily an airline or a livery word ("AJet Airbus A321"). In a title written
    // in words (AIG, with spaces) it must be four characters, alone or followed by letters and digits
    // ("B773UAE", "MD11F", "B738AAL2"); in a title written as a code (no space: FSLTL "FSLTL_GA_VL3_ZZZ",
    // IVAO "F28@GH") three are enough and letters alone will do ("FSLTL_CRJX_ZZZZ"). A short IVAO title
    // that STARTS with any designator is that type ("F16 @CA", "DC6 AAL", "TOR @DE").
    private static string? DesignatorIn(string s, bool isTitle)
    {
        var words = RxToken.Matches(s);
        if (words.Count == 0) return null;
        if (isTitle && s.Length <= 12 && Table.ContainsKey(words[0].Value)) return words[0].Value;
        bool code = !s.Contains(' ');
        // A code title's first word may run the designator into the airline: "GLEXEJA", "F28AGN", "E2USN".
        if (isTitle && code && words[0].Value is { Length: <= 8 } first)
            for (int n = Math.Min(4, first.Length - 1); n >= 2; n--)
                if (first.Skip(n).All(char.IsLetter) && Table.ContainsKey(first[..n])) return first[..n];
        foreach (Match m in words)
        {
            string w = m.Value;
            if (w.Length < (code ? 3 : 4)) continue;
            if (w.Length <= 4)
            {
                if ((code || w.Any(char.IsDigit)) && Table.ContainsKey(w)) return w;
                continue;
            }
            string head = w[..4];
            if (head.Any(char.IsDigit) && w.Skip(4).Any(char.IsLetter) && Table.ContainsKey(head)) return head;
        }
        return null;
    }
}
