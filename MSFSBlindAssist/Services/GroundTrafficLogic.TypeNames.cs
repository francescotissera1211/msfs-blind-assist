namespace MSFSBlindAssist.Services;

// What a traffic aircraft's type is called in a callout. Self-contained (no form, no simulator) so that
// tools/TrafficModelAudit links this file and lists the installed types that have no name here.
internal static partial class GroundTrafficLogic
{
    // ICAO type designator → the name a pilot or a controller says. Airbus keep their number, Boeing
    // airliners lose the B, everything else gets the name it is known by on the radio ("Phenom 300",
    // "King Air 200", "Twin Otter"), with its maker only where the number alone names nothing ("Cessna
    // 182", "Embraer 190"). An entry of "" is no aircraft type at all (a placeholder such as ZZZZ): the
    // aircraft is then named by its airline or its callsign alone. A designator missing here is spoken
    // as it is written, as every one of these was before 2026-11-26 ("Flexjet E55P").
    private static readonly Dictionary<string, string> SpokenTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Airbus
        ["A318"] = "A318", ["A319"] = "A319", ["A320"] = "A320", ["A321"] = "A321",
        ["A19N"] = "A319", ["A20N"] = "A320", ["A21N"] = "A321",
        ["A30B"] = "A300", ["A306"] = "A300", ["A310"] = "A310",
        ["A332"] = "A330", ["A333"] = "A330", ["A338"] = "A330", ["A339"] = "A330",
        ["A342"] = "A340", ["A343"] = "A340", ["A345"] = "A340", ["A346"] = "A340",
        ["A359"] = "A350", ["A35K"] = "A350", ["A388"] = "A380",
        ["BCS1"] = "A220", ["BCS3"] = "A220",
        ["A3ST"] = "Beluga", ["A337"] = "Beluga XL", ["A400"] = "A400M",
        // Boeing
        ["B703"] = "707", ["B712"] = "717", ["B721"] = "727", ["B722"] = "727",
        ["B731"] = "737", ["B732"] = "737", ["B733"] = "737", ["B734"] = "737", ["B735"] = "737",
        ["B736"] = "737", ["B737"] = "737", ["B738"] = "737", ["B739"] = "737",
        ["B37M"] = "737 MAX", ["B38M"] = "737 MAX", ["B39M"] = "737 MAX", ["B3XM"] = "737 MAX",
        ["B741"] = "747", ["B742"] = "747", ["B743"] = "747", ["B744"] = "747", ["B748"] = "747",
        ["B74F"] = "747", ["B74S"] = "747", ["B74D"] = "747", ["B74R"] = "747", ["BLCF"] = "Dreamlifter",
        ["B752"] = "757", ["B753"] = "757",
        ["B762"] = "767", ["B763"] = "767", ["B764"] = "767",
        ["B772"] = "777", ["B773"] = "777", ["B77L"] = "777", ["B77W"] = "777", ["B778"] = "777", ["B779"] = "777",
        ["B788"] = "787", ["B789"] = "787", ["B78X"] = "787",
        // McDonnell Douglas, Lockheed and the older airliners
        ["MD11"] = "MD-11", ["MD90"] = "MD-90", ["DC10"] = "DC-10",
        ["MD81"] = "MD-80", ["MD82"] = "MD-80", ["MD83"] = "MD-80", ["MD87"] = "MD-80", ["MD88"] = "MD-80", ["MD8X"] = "MD-80",
        ["DC91"] = "DC-9", ["DC92"] = "DC-9", ["DC93"] = "DC-9", ["DC94"] = "DC-9", ["DC95"] = "DC-9",
        ["DC3"] = "DC-3", ["DC6"] = "DC-6", ["L101"] = "TriStar", ["CONI"] = "Constellation",
        ["CONC"] = "Concorde", ["CONCORDE"] = "Concorde", ["JU52"] = "Junkers 52",
        // Regional jets
        ["CRJ1"] = "CRJ", ["CRJ2"] = "CRJ", ["CRJ7"] = "CRJ", ["CRJ9"] = "CRJ", ["CRJX"] = "CRJ",
        ["E135"] = "Embraer 135", ["E145"] = "Embraer 145", ["E45X"] = "Embraer 145",
        ["E170"] = "Embraer 170", ["E175"] = "Embraer 175", ["E75L"] = "Embraer 175", ["E75S"] = "Embraer 175",
        ["E190"] = "Embraer 190", ["E195"] = "Embraer 195", ["E290"] = "Embraer 190", ["E295"] = "Embraer 195",
        ["F28"] = "Fokker 28", ["F70"] = "Fokker 70", ["F100"] = "Fokker 100",
        ["B461"] = "BAe 146", ["B462"] = "BAe 146", ["B463"] = "BAe 146",
        ["RJ70"] = "Avro RJ", ["RJ85"] = "Avro RJ", ["RJ1H"] = "Avro RJ",
        ["SU95"] = "Superjet", ["AJ27"] = "ARJ21", ["C919"] = "C919", ["J328"] = "Dornier 328 jet",
        // Airline and commuter turboprops
        ["AT43"] = "ATR 42", ["AT44"] = "ATR 42", ["AT45"] = "ATR 42", ["AT46"] = "ATR 42",
        ["AT72"] = "ATR 72", ["AT73"] = "ATR 72", ["AT75"] = "ATR 72", ["AT76"] = "ATR 72",
        ["DH8A"] = "Dash 8", ["DH8B"] = "Dash 8", ["DH8C"] = "Dash 8", ["DH8D"] = "Dash 8",
        ["DHC2"] = "Beaver", ["DHC6"] = "Twin Otter", ["DHC7"] = "Dash 7",
        ["SF34"] = "Saab 340", ["SB20"] = "Saab 2000", ["F50"] = "Fokker 50", ["D328"] = "Dornier 328",
        ["JS31"] = "Jetstream 31", ["JS32"] = "Jetstream 32", ["JS41"] = "Jetstream 41",
        ["SH36"] = "Shorts 360", ["SW3"] = "Merlin", ["SW4"] = "Metroliner", ["BN2P"] = "Islander",
        ["B190"] = "Beech 1900", ["BE99"] = "Beech 99", ["E110"] = "Bandeirante", ["E120"] = "Brasilia",
        ["C408"] = "SkyCourier", ["F406"] = "F406 Caravan",
        // Antonov, Ilyushin, Tupolev, Beriev
        ["A124"] = "Antonov 124", ["A225"] = "Antonov 225", ["AN2"] = "Antonov 2", ["AN12"] = "Antonov 12",
        ["AN22"] = "Antonov 22", ["AN24"] = "Antonov 24",
        ["IL38"] = "Ilyushin 38", ["IL76"] = "Ilyushin 76", ["IL78"] = "Ilyushin 78", ["IL96"] = "Ilyushin 96",
        ["T134"] = "Tupolev 134", ["T144"] = "Tupolev 144", ["T154"] = "Tupolev 154", ["T160"] = "Tupolev 160",
        ["A50"] = "Beriev A-50", ["BE12"] = "Beriev 12",
        // Business jets
        ["E50P"] = "Phenom 100", ["E55P"] = "Phenom 300", ["E35L"] = "Legacy 600", ["E545"] = "Legacy 450", ["E550"] = "Legacy 500",
        ["CL30"] = "Challenger 300", ["CL35"] = "Challenger 350", ["CL60"] = "Challenger 600",
        ["GLEX"] = "Global Express", ["GL5T"] = "Global 5000", ["GL7T"] = "Global 7500",
        ["GLF3"] = "Gulfstream 3", ["GLF4"] = "Gulfstream 4", ["GLF5"] = "Gulfstream 5", ["GLF6"] = "Gulfstream 650", ["G280"] = "Gulfstream 280",
        ["C510"] = "Citation Mustang", ["C525"] = "Citation Jet", ["C25A"] = "Citation CJ2", ["C25B"] = "Citation CJ3", ["C25C"] = "Citation CJ4",
        ["C550"] = "Citation 2", ["C560"] = "Citation 5", ["C56X"] = "Citation Excel", ["C680"] = "Citation Sovereign",
        ["C68A"] = "Citation Latitude", ["C700"] = "Citation Longitude", ["C750"] = "Citation 10",
        ["H25B"] = "Hawker", ["BE40"] = "Beechjet", ["PRM1"] = "Beech Premier", ["WW24"] = "Westwind",
        ["LJ25"] = "Learjet 25", ["LJ31"] = "Learjet 31", ["LJ35"] = "Learjet 35", ["LJ40"] = "Learjet 40", ["LJ45"] = "Learjet 45",
        ["LJ55"] = "Learjet 55", ["LJ60"] = "Learjet 60", ["LJ70"] = "Learjet 70", ["LJ75"] = "Learjet 75",
        ["FA10"] = "Falcon 10", ["FA20"] = "Falcon 20", ["FA50"] = "Falcon 50", ["F900"] = "Falcon 900", ["F2TH"] = "Falcon 2000",
        ["FA7X"] = "Falcon 7X", ["FA8X"] = "Falcon 8X",
        ["HDJT"] = "HondaJet", ["SF50"] = "Vision Jet", ["PC24"] = "PC-24", ["L29A"] = "JetStar",
        // Private turboprops
        ["BE9L"] = "King Air 90", ["BE10"] = "King Air 100", ["BE20"] = "King Air 200", ["B350"] = "King Air 350",
        ["PC12"] = "PC-12", ["PC6"] = "Porter", ["TBM7"] = "TBM", ["TBM8"] = "TBM", ["TBM9"] = "TBM",
        ["C208"] = "Caravan", ["C441"] = "Conquest", ["KODI"] = "Kodiak", ["P180"] = "Avanti", ["MU2"] = "MU-2",
        ["EPIC"] = "Epic", ["P46T"] = "Piper Meridian", ["G21T"] = "Turbo Goose",
        // Piston aircraft
        ["C140"] = "Cessna 140", ["C152"] = "Cessna 152", ["C170"] = "Cessna 170", ["C172"] = "Cessna 172", ["C72R"] = "Cessna 172",
        ["C177"] = "Cessna 177", ["C77R"] = "Cessna 177", ["C182"] = "Cessna 182", ["C82R"] = "Cessna 182", ["C185"] = "Cessna 185",
        ["C206"] = "Cessna 206", ["C207"] = "Cessna 207", ["C210"] = "Cessna 210", ["P210"] = "Cessna 210", ["C337"] = "Cessna 337",
        ["C402"] = "Cessna 402", ["C404"] = "Cessna 404", ["C414"] = "Cessna 414", ["C421"] = "Cessna 421",
        ["P28A"] = "Piper Cherokee", ["P28R"] = "Piper Arrow", ["P28S"] = "Piper Arrow", ["P28U"] = "Piper Arrow",
        ["PA24"] = "Piper Comanche", ["PA31"] = "Piper Navajo", ["PA34"] = "Piper Seneca", ["PA44"] = "Piper Seminole",
        ["PA46"] = "Piper Malibu", ["J3"] = "Piper Cub", ["AEST"] = "Aerostar",
        ["SR20"] = "Cirrus SR20", ["SR22"] = "Cirrus SR22",
        ["DA40"] = "Diamond DA40", ["DA42"] = "Diamond DA42", ["DA62"] = "Diamond DA62", ["DR40"] = "Robin DR400",
        ["BE35"] = "Bonanza", ["BE36"] = "Bonanza", ["BE58"] = "Baron", ["BE60"] = "Beech Duke", ["B60T"] = "Beech Duke",
        ["M20P"] = "Mooney", ["M20T"] = "Mooney", ["AA5"] = "Grumman AA-5", ["G115"] = "Grob 115", ["VL3"] = "VL-3",
        ["S205"] = "SIAI 205", ["S208"] = "SIAI 208", ["F260"] = "SF-260", ["SV4"] = "Stampe", ["BRAV"] = "Bravo",
        ["AB11"] = "Aero Boero", ["A5"] = "Icon A5", ["CC19"] = "Carbon Cub", ["CP10"] = "CAP 10", ["E300"] = "Extra 300",
        ["FDCT"] = "Flight Design CT", ["PTS1"] = "Pitts", ["SAVG"] = "Savage", ["SPIT"] = "Spitfire",
        ["LGEZ"] = "Long-EZ", ["LONGEZ"] = "Long-EZ",
        ["ULAC"] = "ultralight", ["GLID"] = "glider", ["DGF"] = "glider", ["PAR"] = "paraglider", ["HAB"] = "balloon", ["SHIP"] = "ship",
        // Military transports, tankers and patrol aircraft
        ["C130"] = "C-130", ["C160"] = "Transall", ["C17"] = "C-17", ["C5"] = "C-5", ["CN35"] = "CN-235", ["C295"] = "C-295",
        ["K35R"] = "KC-135", ["E2"] = "E-2", ["E3"] = "E-3", ["E737"] = "Wedgetail", ["P8"] = "P-8", ["Q4"] = "Global Hawk",
        ["CL2T"] = "CL-415", ["C215"] = "CL-215", ["V22"] = "V-22",
        // Military jets and trainers
        ["F4"] = "F-4", ["F14"] = "F-14", ["F16"] = "F-16", ["F18F"] = "F-18", ["F18H"] = "F-18", ["F18S"] = "F-18", ["FA18E"] = "F-18", ["F22"] = "F-22",
        ["F35"] = "F-35", ["F35A"] = "F-35", ["F35B"] = "F-35", ["F35C"] = "F-35", ["F117"] = "F-117", ["A4"] = "A-4",
        ["B1"] = "B-1", ["B2"] = "B-2", ["U2"] = "U-2", ["SR71"] = "SR-71", ["VULC"] = "Vulcan", ["BUC"] = "Buccaneer",
        ["HAR"] = "Harrier", ["TOR"] = "Tornado", ["EUFI"] = "Eurofighter", ["RFAL"] = "Rafale", ["MIR2"] = "Mirage 2000",
        ["ETAR"] = "Etendard", ["KFIR"] = "Kfir", ["MG29"] = "MiG-29", ["AJET"] = "Alpha Jet",
        ["T38"] = "T-38", ["TEX2"] = "Texan 2", ["PC21"] = "PC-21", ["L39"] = "L-39", ["M339"] = "MB-339", ["C101"] = "C-101",
        ["FOUG"] = "Fouga Magister", ["FOUGA"] = "Fouga Magister",
        // Helicopters
        ["EC35"] = "EC135", ["EC45"] = "EC145", ["EC25"] = "EC225", ["EC75"] = "H175", ["H160"] = "H160",
        ["AS50"] = "AS350", ["AS32"] = "Super Puma", ["AS65"] = "Dauphin", ["A109"] = "AW109",
        ["B06"] = "JetRanger", ["B407"] = "Bell 407", ["B214"] = "Bell 214", ["B222"] = "Bell 222", ["UH1"] = "Huey",
        ["H60"] = "Black Hawk", ["H64"] = "Apache", ["H53"] = "CH-53", ["CH47"] = "Chinook", ["S61"] = "Sea King", ["S64"] = "Skycrane",
        ["MI8"] = "Mi-8", ["MI26"] = "Mi-26", ["MD52"] = "MD 520", ["R22"] = "Robinson R22", ["R44"] = "Robinson R44", ["G2CA"] = "Cabri",
        // The simulator's own stand-in airliners, and the placeholders that name no type.
        ["GENERIC"] = "airliner", ["ASOBO2J"] = "airliner", ["ASOBO4J"] = "airliner",
        ["ZZZZ"] = "", ["ZZZC"] = "", ["UNIV"] = "",
    };

    /// <summary>
    /// The spoken name of a type designator (or of a type written out, "Concorde"): null when it has none
    /// here, "" when it is a placeholder that names no type. Spaces and hyphens are ignored ("PA-44").
    /// </summary>
    public static string? SpokenTypeName(string? designator)
    {
        string d = (designator ?? "").Trim();
        if (d.Length == 0) return null;
        if (SpokenTypes.TryGetValue(d, out var spoken)) return spoken;
        string joined = d.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return joined.Length != d.Length && SpokenTypes.TryGetValue(joined, out spoken) ? spoken : null;
    }
}
