// Ground-traffic distances are between the two aircraft's NEAREST POINTS: these pin the pieces that make
// that so — the type read from what a traffic sweep delivers, the gap between two outlines, the outline
// measured from a model's own glTF, the installed-model index, and the monitor speaking the gap.

using System.Globalization;
using System.Text;
using MSFSBlindAssist.Services;
using Xunit;
using static MSFSBlindAssist.Tests.GroundTrafficHarness;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficAircraftSizesTests
{
    // Every row is a real sweep entry or installed title (OMDB, 2026-11-26; AIG, FSLTL, IVAO, SayIntentions).
    [Theory]
    [InlineData("$$:Boeing", "$$:Triple 7", "AIGAIM_Emirates Boeing 777-300ER - Fwd Dome This Flag Will Always Fly", "B77W")]
    [InlineData("$$:Boeing", "$$:Triple 7", "AIGAIM_Jin Air Boeing 777-200ER", "B772")]
    [InlineData("$$:Boeing", "$$:Triple 7", "AIGAIM_EVA Air Cargo Boeing 777-200F", "B77L")]
    [InlineData("$$:Boeing", "$$:737", "AIGAIM_SpiceJet Boeing 737 MAX 8 - VT-MXC", "B38M")]
    [InlineData("$$:Boeing", "$$:737", "AIGAIM_Ruffin Companies Boeing BBJ1 WL - N43PR", "B737")]
    [InlineData("$$:Airbus", "$$:A321", "AIGAIM_IndiGo Airbus A321-200neo - ACF CFM", "A321")]
    [InlineData("$$:Airbus", "$$:A350", "AIGAIM_Emirates Airbus A350-900 New WL", "A359")]
    [InlineData("$$:Airbus", "$$:A330", "AIGAIM_Swiss Airbus A330-300", "A333")]
    [InlineData("Boeing", "B777-300", "B773UAE", "B773")]                                   // SayIntentions
    [InlineData("TT:ATCCOM.ATC_NAME AIRBUS.0.text", "A320", "FSLTL_FAIB_A20N_Aircalin", "A20N")]
    [InlineData("", "", "FSLTL_FSPXAI_MD11F_Aerojet_Cargo_Fictional", "MD11")]
    [InlineData("", "", "FSLTL_CRJX_ZZZZ", "CRJX")]
    [InlineData("ATCCOM.ATC_NAME AIRBUS.0.text", "ATCCOM.AC_MODEL A320.0.text", "Asobo PassiveAircraft A320 CEO", "A320")]
    [InlineData("A20N", "A320 NEO CFML", "A20N@SN", "A20N")]                                 // IVAO
    [InlineData("", "", "B738AAL2", "B738")]
    [InlineData("", "", "F28@GH", "F28")]
    [InlineData("", "", "GLEXEJA", "GLEX")]
    [InlineData("", "", "DC6 AAL", "DC6")]
    // An airline or a livery note that holds a type word never wins over the type itself.
    [InlineData("", "", "AIGAIM_AJet Airbus A321-200neo - ACF", "A321")]
    [InlineData("", "", "AIGAIM_Global Jet International Hawker 800XP - VH-MED", "H25B")]
    [InlineData("", "", "AIGAIM_Baron Aviation Cessna 208B Super Cargomaster", "C208")]
    [InlineData("", "", "AIGAIM_Alpine Air Express Beechcraft 1900D - Gulfstream Intl N231YV", "B190")]
    [InlineData("", "", "AIGAIM_Fastjet Zimbabwe Embraer ERJ-145 - C9-BBJ", "E145")]
    [InlineData("", "", "AIGAIM_Helvetic Airways Embraer EMB-190E2", "E290")]
    [InlineData("", "", "AIGAIM_Aero Embraer ERJ-135", "E135")]
    [InlineData("", "", "Generic Airliner Twin Engines Asobo 00", "")]
    public void The_type_is_read_from_the_title_then_the_ATC_type_and_model(string type, string model, string title, string expected)
        => Assert.Equal(expected, GroundTrafficAircraftSizes.ResolveIcao(type, model, title));

    [Fact]
    public void A_known_type_takes_its_published_size_whatever_wing_span_the_simulator_reports()
    {
        // The SayIntentions 777 at OMDB reported a 15.48 m wing span.
        var size = GroundTrafficAircraftSizes.Resolve("Boeing", "B777-300", "B773UAE", 15.48);
        Assert.Equal(AircraftSizeSource.Table, size.Source);
        Assert.Equal(73.86, size.LengthM, 2);
        Assert.Equal(60.93, size.WingspanM, 2);
    }

    [Fact]
    public void An_unknown_type_is_scaled_from_a_plausible_wing_span_and_is_the_reference_widebody_without_one()
    {
        var scaled = GroundTrafficAircraftSizes.Resolve("$$:Generic", "$$:", "Generic Airliner Twin Engines Asobo 00", 30.0);
        Assert.Equal(AircraftSizeSource.WingSpan, scaled.Source);
        Assert.Equal(30.0 * GroundTrafficAircraftSizes.LengthPerWingspan, scaled.LengthM, 6);

        Assert.Equal(GroundTrafficAircraftSizes.Default, GroundTrafficAircraftSizes.Resolve("", "", "", 0.0));
        Assert.Equal(GroundTrafficAircraftSizes.Default, GroundTrafficAircraftSizes.Resolve("", "", "", 250.0));
    }

    [Fact]
    public void The_own_aircraft_takes_its_type_s_length_and_its_own_reported_wing_span()
    {
        var own = GroundTrafficAircraftSizes.ResolveOwn("A359", "A350-900 (Default Cabin)", 64.75);
        Assert.Equal(66.80, own.LengthM, 2);
        Assert.Equal(64.75, own.WingspanM, 2);
    }
}

public class GroundTrafficHullGapTests
{
    private static AircraftHull Hull(double lengthM, double spanM)
        => AircraftHull.FromSize(new AircraftSize(lengthM, spanM, AircraftSizeSource.Table, "TEST"));

    [Fact]
    public void In_line_the_gap_is_from_the_own_nose_to_the_other_tail()
    {
        var own = Hull(70, 60);
        var other = Hull(40, 36);
        // Both heading north, the other 100 m ahead.
        double gap = GroundTrafficLogic.HullGapM(own, 0, other, 0, 0, 100);
        Assert.Equal(100 - own.NoseM - other.TailM, gap, 6);
    }

    [Fact]
    public void Pushing_back_toward_traffic_behind_the_gap_is_from_the_own_tail_to_its_nose()
    {
        var own = Hull(70, 60);
        var other = Hull(40, 36);
        // The other is 80 m behind, facing the same way: its nose points at the own tail.
        double gap = GroundTrafficLogic.HullGapM(own, 0, other, 0, 0, -80);
        Assert.Equal(80 - own.TailM - other.NoseM, gap, 6);
    }

    [Fact]
    public void Side_by_side_the_gap_is_wingtip_to_wingtip_and_wings_that_overlap_are_zero()
    {
        var own = Hull(70, 60);
        var other = Hull(40, 36);
        Assert.Equal(100 - 30 - 18, GroundTrafficLogic.HullGapM(own, 90, other, 90, 0, 100), 6);
        Assert.Equal(0.0, GroundTrafficLogic.HullGapM(own, 90, other, 90, 0, 40), 6);
    }

    [Fact]
    public void Traffic_crossing_ahead_is_measured_to_the_wingtip_it_points_at_the_own_nose()
    {
        var own = Hull(70, 60);
        var other = Hull(40, 36);
        // The other sits 100 m ahead, across the nose (heading east while the own aircraft heads north):
        // its wing reaches 18 m back toward the own nose.
        Assert.Equal(100 - own.NoseM - 18, GroundTrafficLogic.HullGapM(own, 0, other, 90, 0, 100), 6);
    }

    [Fact]
    public void A_hull_reaches_its_nose_ahead_its_tail_behind_and_half_its_span_to_each_side()
    {
        var hull = Hull(50, 40);
        Assert.Equal(hull.NoseM, hull.ExtentToward(0), 6);
        Assert.Equal(hull.TailM, hull.ExtentToward(180), 6);
        Assert.Equal(20.0, hull.ExtentToward(90), 6);
        Assert.Equal(20.0, hull.ExtentToward(-90), 6);
        Assert.Equal(50 * AircraftHull.NoseFraction, hull.NoseM, 6);
    }
}

public class GroundTrafficModelReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-model-" + Guid.NewGuid().ToString("N"));

    public GroundTrafficModelReaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    // One box-shaped mesh per part: x from -halfWidth to halfWidth, z from zMin to zMax, as two triangles.
    private sealed record Part(string Name, double HalfWidth, double ZMin, double ZMax, double XShift = 0,
        double[]? Translation = null, bool Skinned = false, string? AnimatedBy = null);

    private string WriteModel(string name, params Part[] parts)
    {
        var bin = new List<byte>();
        var views = new StringBuilder();
        var accessors = new StringBuilder();
        var meshes = new StringBuilder();
        var nodes = new StringBuilder();
        var animations = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            int offset = bin.Count;
            float[] v =
            {
                (float)(p.XShift - p.HalfWidth), 0, (float)p.ZMin, (float)(p.XShift + p.HalfWidth), 0, (float)p.ZMin,
                (float)(p.XShift + p.HalfWidth), 0, (float)p.ZMax, (float)(p.XShift - p.HalfWidth), 0, (float)p.ZMax,
            };
            foreach (float f in v) bin.AddRange(BitConverter.GetBytes(f));
            int indexOffset = bin.Count;
            foreach (ushort ix in new ushort[] { 0, 1, 2, 0, 2, 3 }) bin.AddRange(BitConverter.GetBytes(ix));
            if (i > 0) { views.Append(','); accessors.Append(','); meshes.Append(','); nodes.Append(','); }
            views.Append(CultureInfo.InvariantCulture, $"{{\"buffer\":0,\"byteOffset\":{offset},\"byteLength\":48}},{{\"buffer\":0,\"byteOffset\":{indexOffset},\"byteLength\":12}}");
            accessors.Append(CultureInfo.InvariantCulture, $"{{\"bufferView\":{i * 2},\"componentType\":5126,\"count\":4,\"type\":\"VEC3\"}},{{\"bufferView\":{i * 2 + 1},\"componentType\":5123,\"count\":6,\"type\":\"SCALAR\"}}");
            meshes.Append(CultureInfo.InvariantCulture, $"{{\"primitives\":[{{\"attributes\":{{\"POSITION\":{i * 2}}},\"indices\":{i * 2 + 1}}}]}}");
            string t = p.Translation is { } tr ? string.Create(CultureInfo.InvariantCulture, $",\"translation\":[{tr[0]},{tr[1]},{tr[2]}]") : "";
            string skin = p.Skinned ? ",\"skin\":0" : "";
            nodes.Append(CultureInfo.InvariantCulture, $"{{\"name\":\"{p.Name}\",\"mesh\":{i}{t}{skin}}}");
            if (p.AnimatedBy != null)
            {
                if (animations.Length > 0) animations.Append(',');
                animations.Append(CultureInfo.InvariantCulture, $"{{\"name\":\"{p.AnimatedBy}\",\"channels\":[{{\"sampler\":0,\"target\":{{\"node\":{i},\"path\":\"weights\"}}}}],\"samplers\":[{{\"input\":0,\"output\":0}}]}}");
            }
        }
        string roots = string.Join(",", Enumerable.Range(0, parts.Length));
        string skins = parts.Any(p => p.Skinned) ? ",\"skins\":[{\"joints\":[0]}]" : "";
        string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[" + roots + "]}],\"nodes\":[" + nodes
            + "],\"meshes\":[" + meshes + "],\"accessors\":[" + accessors + "],\"bufferViews\":[" + views
            + "],\"buffers\":[{\"uri\":\"" + name + ".bin\",\"byteLength\":" + bin.Count.ToString(CultureInfo.InvariantCulture) + "}]"
            + (animations.Length > 0 ? ",\"animations\":[" + animations + "]" : "") + skins + "}";
        File.WriteAllBytes(Path.Combine(_dir, name + ".bin"), bin.ToArray());
        string path = Path.Combine(_dir, name + ".gltf");
        File.WriteAllText(path, json);
        return path;
    }

    private static readonly Part Fuselage = new("fuselage", 2, -22, 18);
    private static readonly Part Wing = new("wing", 17, -4, 2);

    [Fact]
    public void A_model_measures_to_its_nose_its_tail_and_its_wingtips_from_its_origin()
    {
        var outline = GroundTrafficModelReader.Measure(WriteModel("plain", Fuselage, Wing))!;
        Assert.Equal(18.0, outline.NoseM, 2);
        Assert.Equal(22.0, outline.TailM, 2);
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        // The outline keeps the corners: the nose, the tail and both wingtips.
        Assert.Contains(outline.Polygon, p => Math.Abs(p.X - 17.0) < 0.01);
        Assert.Contains(outline.Polygon, p => Math.Abs(p.X + 17.0) < 0.01);
        Assert.Equal(18.0, outline.Polygon.Max(p => p.Z), 2);
        Assert.Equal(-22.0, outline.Polygon.Min(p => p.Z), 2);
    }

    [Fact]
    public void A_node_s_own_translation_moves_its_part()
    {
        var outline = GroundTrafficModelReader.Measure(WriteModel("moved", Fuselage,
            new Part("wing", 17, -4, 2, Translation: new[] { 0.0, 0.0, -10.0 })))!;
        // The wing now spans z -14 to -8: the widest stations sit there.
        int widest = Enumerable.Range(0, outline.HalfWidths.Count).First(i => outline.HalfWidths[i] > 16.9f);
        Assert.Equal(-14.0, -outline.TailM + widest * AircraftOutline.StationM, 0);
    }

    [Fact]
    public void A_skinned_mesh_s_own_node_transform_is_not_applied()
    {
        string plain = WriteModel("unskinned", Fuselage, new Part("wing", 17, -4, 2, Translation: new[] { 30.0, 0.0, 0.0 }));
        string skinned = WriteModel("skinned", Fuselage, new Part("wing", 17, -4, 2, Translation: new[] { 30.0, 0.0, 0.0 }, Skinned: true));
        // Moved 30 m to one side, a wing reaches only one side and is dropped; the skinned one stays put.
        Assert.Equal(2.0, GroundTrafficModelReader.Measure(plain)!.HalfSpanM, 2);
        Assert.Equal(17.0, GroundTrafficModelReader.Measure(skinned)!.HalfSpanM, 2);
    }

    [Fact]
    public void The_first_level_of_detail_with_no_skinned_mesh_is_the_one_measured()
    {
        string lod0 = WriteModel("lod0", Fuselage, new Part("wing", 25, -4, 2, Skinned: true));
        string lod1 = WriteModel("lod1", Fuselage, Wing);
        Assert.Equal(17.0, GroundTrafficModelReader.Measure(new[] { lod0, lod1 })!.HalfSpanM, 2);
        Assert.Equal(25.0, GroundTrafficModelReader.Measure(new[] { lod0 })!.HalfSpanM, 2);
    }

    [Theory]
    [InlineData("ASO_Pushback")]          // IVAO draws its ground service inside the aircraft model
    [InlineData("groundservice")]
    [InlineData("custom_part_vis_blocks_VC")]
    public void Ground_equipment_inside_a_model_is_not_airframe(string name)
    {
        var outline = GroundTrafficModelReader.Measure(WriteModel("service", Fuselage, Wing, new Part(name, 2, 15, 26)))!;
        Assert.Equal(18.0, outline.NoseM, 2);
    }

    [Fact]
    public void A_part_a_gear_animation_moves_is_left_out()
    {
        // FSLTL's 777: its nose gear rests 6 m ahead of the nose.
        var outline = GroundTrafficModelReader.Measure(WriteModel("gear", Fuselage, Wing,
            new Part("node14_anim_c_gear", 5, 12, 24, AnimatedBy: "GEAR_center")))!;
        Assert.Equal(18.0, outline.NoseM, 2);
    }

    [Fact]
    public void A_part_on_one_side_only_and_a_part_detached_from_the_body_are_dropped()
    {
        var outline = GroundTrafficModelReader.Measure(WriteModel("stray", Fuselage, Wing,
            new Part("fuel", 4, -2, 2, XShift: 30), new Part("far", 2, 40, 44)))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);
    }

    [Fact]
    public void A_file_that_is_not_a_model_and_a_model_that_is_not_aircraft_sized_measure_as_nothing()
    {
        string junk = Path.Combine(_dir, "junk.gltf");
        File.WriteAllText(junk, "not json");
        Assert.Null(GroundTrafficModelReader.Measure(junk));
        Assert.Null(GroundTrafficModelReader.Measure(Path.Combine(_dir, "missing.gltf")));
        Assert.Null(GroundTrafficModelReader.Measure(WriteModel("tiny", new Part("speck", 0.1, -0.2, 0.2))));
    }

    // ── The installed-model index and the catalog ────────────────────────────────────────────────────

    private string InstallAircraft(string folder, string cfg, string? modelFolder = null, string? modelCfg = null)
    {
        string dir = Path.Combine(_dir, "Community", "pkg", "SimObjects", "Airplanes", folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "aircraft.cfg"), cfg);
        if (modelFolder != null)
        {
            Directory.CreateDirectory(Path.Combine(dir, modelFolder));
            File.WriteAllText(Path.Combine(dir, modelFolder, "model.cfg"), modelCfg);
        }
        return dir;
    }

    [Fact]
    public void A_livery_s_title_leads_to_the_model_its_base_container_holds()
    {
        string baseDir = InstallAircraft("FSLTL_A20N", "[FLTSIM.0]\ntitle = \"FSLTL_A20N_ZZZZ\"\nmodel = \"CFML\"\n",
            "model.CFML", "[models]\nnormal=A320.xml\n");
        string gltf = WriteModel("A320_LOD0", Fuselage, Wing);
        File.Copy(gltf, Path.Combine(baseDir, "model.CFML", "A320_LOD0.gltf"));
        File.Copy(Path.ChangeExtension(gltf, ".bin"), Path.Combine(baseDir, "model.CFML", "A320_LOD0.bin"));
        File.WriteAllText(Path.Combine(baseDir, "model.CFML", "A320.xml"),
            "<ModelInfo><LODS><LOD minSize=\"10\" ModelFile=\"A320_LOD0.gltf\"/><LOD ModelFile=\"gone.gltf\"/></LODS></ModelInfo>");
        InstallAircraft("FSLTL_A20N_BAW",
            "[VARIATION]\nbase_container = \"..\\FSLTL_A20N\"\n[fltsim.0]\ntitle = \"FSLTL A20N BAW British Airways\" ; Variation name\nmodel = \"BAW\" ; model folder\n",
            "model.BAW", "[models]\nnormal=..\\..\\FSLTL_A20N\\model.CFML\\A320.xml\n");

        var index = GroundTrafficModelCatalog.BuildIndex(_dir);
        Assert.Equal(2, index.Count);
        foreach (string title in new[] { "FSLTL_A20N_ZZZZ", "fsltl a20n baw british airways" })
        {
            var lods = GroundTrafficModelCatalog.LodPaths(index[title]);
            Assert.Equal("A320_LOD0.gltf", Path.GetFileName(Assert.Single(lods)));
        }
    }

    [Fact]
    public void The_catalog_measures_a_title_in_the_background_and_answers_an_unknown_one_with_nothing()
    {
        string dir = InstallAircraft("AIG_B77W", "[FLTSIM.0]\ntitle=AIGAIM_Emirates Boeing 777-300ER\nmodel=\n", "model", "[models]\nnormal=m.gltf\n");
        string gltf = WriteModel("m", Fuselage, Wing);
        File.Copy(gltf, Path.Combine(dir, "model", "m.gltf"));
        File.Copy(Path.ChangeExtension(gltf, ".bin"), Path.Combine(dir, "model", "m.bin"));
        string cache = Path.Combine(_dir, "cache");

        using (var catalog = new GroundTrafficModelCatalog(cache, () => _dir))
        {
            Assert.Null(catalog.TryGet("AIGAIM_Emirates Boeing 777-300ER"));      // queued, never blocks
            Assert.Null(catalog.TryGet("Not installed"));
            Assert.True(SpinWait.SpinUntil(() => catalog.IsSettled("AIGAIM_Emirates Boeing 777-300ER") && catalog.IsSettled("Not installed"), 10_000));
            Assert.Equal(17.0, catalog.TryGet("AIGAIM_Emirates Boeing 777-300ER")!.HalfSpanM, 2);
            Assert.Null(catalog.TryGet("Not installed"));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(cache, "outlines.json")), 10_000));
        }

        // A second run reads the kept outline: the model file itself is gone and is not needed.
        File.Delete(Path.Combine(dir, "model", "m.bin"));
        using var second = new GroundTrafficModelCatalog(cache, () => _dir);
        second.TryGet("AIGAIM_Emirates Boeing 777-300ER");
        Assert.True(SpinWait.SpinUntil(() => second.IsSettled("AIGAIM_Emirates Boeing 777-300ER"), 10_000));
        Assert.Equal(18.0, second.TryGet("AIGAIM_Emirates Boeing 777-300ER")!.NoseM, 2);
    }
}

public class GroundTrafficGapSpeechTests
{
    private static SimConnect.AiTrafficDataEventArgs Typed(uint id, double eastM, string airline, string type, string title, double headingDeg = 90.0)
    {
        var ac = Ac(id, eastM, 0, 0, airline, "");
        ac.AircraftType = type;
        ac.Title = title;
        ac.HeadingMagnetic = headingDeg;
        return ac;
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void A_heavy_80_metres_behind_centre_to_centre_is_spoken_as_the_few_metres_it_is(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            // Pushing back in a 777-300ER with an A380 80 m behind, centre to centre, which is what the
            // monitor used to speak. The own tail is 39.9 m behind the own point (the nose 0.46 of 73.86 m
            // ahead of it) and the A380's nose 33.5 m ahead of its own: 80 - 39.9 - 33.5 = 6.6 m, 22 ft.
            var h = new GroundTrafficHarness();
            h.UseRealSizes("B77W", 212.6);
            h.Sim.Position = Own(0, 0);
            h.Sim.Traffic.Add(Typed(1, -80, "Emirates", "$$:Airbus", "AIGAIM_Emirates Airbus A380-800 - EA"));
            h.Tick(3);
            Assert.Contains("Emirates A380, behind, 25 feet", h.Monitor.GetNearestTrafficSummary());
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void The_summary_s_nearest_aircraft_are_the_nearest_by_gap()
    {
        // OMDB, 2026-11-26, five aircraft spawned round a parked A350: the summary listed a Citation 67 m
        // away (45 m clear) and left out the 777 whose nose was 25 m from the tail, 100 m centre to centre.
        var h = new GroundTrafficHarness();
        h.UseRealSizes("B77W", 212.6);
        h.Sim.Position = Own(0, 0);
        h.Sim.Traffic.Add(Typed(1, -100, "Emirates", "A388", ""));                       // behind: 27 m clear
        var twin = Typed(2, 0, "", "BE58", "BE58"); twin.Latitude = -70 * M;             // to the right: 32 m
        h.Sim.Traffic.Add(twin);
        var far = Typed(3, 0, "Qantas", "A388", ""); far.Latitude = 150 * M;             // to the left: 78 m
        h.Sim.Traffic.Add(far);
        var farther = Typed(4, 0, "", "BE58", "BE58"); farther.Latitude = -120 * M;     // 82 m, nearer by centre than the Qantas
        h.Sim.Traffic.Add(farther);
        h.Tick(3);
        string summary = h.Monitor.GetNearestTrafficSummary();
        int a380 = summary.IndexOf("Emirates A380, behind", StringComparison.Ordinal),
            be58 = summary.IndexOf("Baron, to the right", StringComparison.Ordinal),
            qantas = summary.IndexOf("Qantas A380, to the left", StringComparison.Ordinal);
        Assert.True(a380 >= 0 && be58 > a380 && qantas > be58, summary);
        Assert.StartsWith("3 aircraft nearby.", summary);
    }

    [Fact]
    public void A_light_twin_75_metres_ahead_is_no_longer_Stop()
    {
        // ground_traffic.log, 2026-10-08: "Stop, BE58 very close, ahead and to the right, 75 metres." A Baron
        // is 9 m long: 75 m centre to centre from an A320 is a 60 m gap, inside neither Stop nor Slow down.
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        h.Sim.Position = Own(0, 0);
        h.Sim.Traffic.Add(Typed(1, 75, "", "BE58", "BE58"));
        h.Tick(6);
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Stop", StringComparison.Ordinal));
        Assert.Contains(h.Said.All, m => m.Contains("Baron, ahead, 175 feet, stopped."));
    }

    [Fact]
    public void An_aircraft_of_unknown_type_is_still_taken_as_the_reference_widebody()
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.UseRealSizes();
        h.Sim.Position = Own(0, 0);
        h.Sim.Traffic.Add(Typed(1, 240 / GroundTrafficLogic.FeetPerMetre, "", "", ""));
        h.Tick(6);
        // 240 ft centre to centre, less the 200 ft of two reference half-fuselages.
        Assert.Contains(h.Said.All, m => m.StartsWith("Stop, traffic very close, ahead, 50 feet", StringComparison.Ordinal));
    }
}

public class AiTrafficTypeTests
{
    // What the TCAS window and the ground-traffic callouts are handed as an aircraft's type.
    [Theory]
    [InlineData("$$:Boeing", "$$:Triple 7", "AIGAIM_Emirates Boeing 777-300ER - Fwd Dome", "B77W")]
    [InlineData("$$:Airbus", "$$:A320", "AIGAIM_IndiGo Airbus A320-200neo - PW", "A320")]
    [InlineData("ATCCONN", "B77W.mdl", "", "B77W")]
    [InlineData("TT:ATCCOM.ATC_NAME AIRBUS.0.text", "A320", "FSLTL_FAIB_A20N_Aircalin", "A20N")]
    // A type no table knows keeps its ATC type, without AIG's marker.
    [InlineData("$$:Generic", "$$:", "Generic Airliner Twin Engines Asobo 00", "Generic")]
    public void A_traffic_aircraft_s_type_is_the_one_its_title_names(string atcType, string atcModel, string title, string expected)
        => Assert.Equal(expected, SimConnect.SimConnectManager.ResolveAiAircraftType(atcType, atcModel, title));
}

public class MeasuredOutlineGuardTests
{
    private static AircraftOutline Outline(double tailM, double noseM, float halfSpanM)
        => new(tailM, noseM, Enumerable.Repeat(halfSpanM, (int)((tailM + noseM) / AircraftOutline.StationM)).ToArray());

    [Fact]
    public void A_measured_outline_far_from_its_type_s_published_size_is_not_believed()
    {
        var a320 = GroundTrafficAircraftSizes.Resolve("A320", "", "", 0.0);
        Assert.True(GroundTrafficAircraftSizes.Agrees(Outline(21.1, 16.5, 17.1f), a320));     // the FAIB A320, no sharklets
        Assert.False(GroundTrafficAircraftSizes.Agrees(Outline(25.7, 13.5, 6.8f), a320));     // the Fenix with no wings placed
        Assert.False(GroundTrafficAircraftSizes.Agrees(Outline(40.0, 30.0, 17.9f), a320));    // far too long
    }

    [Fact]
    public void An_unknown_type_has_nothing_to_hold_a_measurement_against()
        => Assert.True(GroundTrafficAircraftSizes.Agrees(Outline(5.0, 3.0, 6.0f), GroundTrafficAircraftSizes.Default));
}

public class GroundTrafficConvergingGapTests
{
    private static SimConnect.AiTrafficDataEventArgs Crosser(double eastM, double northM, string type, double gsKts)
    {
        var ac = Ac(1, eastM, northM, gsKts, "Delta", "DAL1", headingDeg: 0);
        ac.AircraftType = type;
        return ac;
    }

    // An aircraft crossing from the right whose reported point passes 46 m from the own one. A light twin
    // then has about 30 m of clear space to an A320; an A380, with a 40 m half-span, has under 15 m.
    // Measured between reported points both were "converging" (46 m is inside the old 60 m).
    [Theory]
    [InlineData("BE58", false)]
    [InlineData("A388", true)]
    public void Converging_is_judged_on_the_gap_the_two_outlines_will_have(string type, bool expectConverging)
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        // Own aircraft rolls east at 10 kt; the crosser heads north at 10 kt.
        var crosser = Crosser(165, -100, type, 10);
        h.Sim.Traffic.Add(crosser);
        double ownM = 0, north = -100;
        for (int s = 0; s < 9; s++)
        {
            ownM += 10 * 0.514444;
            north += 10 * 0.514444;
            crosser.Latitude = north * M;
            h.Sim.Position = Own(ownM, 10);
            h.Tick();
        }
        Assert.Equal(expectConverging, h.Said.All.Any(m => m.Contains("converging")));
    }
}

public class GroundTrafficPushbackTests
{
    // The own aircraft, nose east, moved along its own axis one simulated second at a time.
    private static void Roll(GroundTrafficHarness h, ref double eastM, double kts, int seconds)
    {
        for (int s = 0; s < seconds; s++)
        {
            eastM += kts * 0.514444;
            h.Sim.Position = Own(eastM, Math.Abs(kts));
            h.Tick();
        }
    }

    private static GroundTrafficHarness Stand(out double eastM)
    {
        // A route loaded at the stand: it leads away ahead of the nose, as a route does before a pushback.
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.UseRealSizes("B77W", 212.6);
        eastM = 0;
        h.Sim.Position = Own(0, 0);
        return h;
    }

    [Fact]
    public void Pushed_back_toward_an_aircraft_behind_the_pilot_is_told_to_slow_down_and_to_stop()
    {
        // OMDB, 2026-11-26: an A350 pushed back 53 m toward a 777 stopped across the taxilane behind its
        // stand, from 59 m of clear space to 25, and nothing was said. "Slow down" and "Stop" were given
        // only for the arc ahead of the NOSE, and the aircraft was taken to be moving nose-first.
        var h = Stand(out double east);
        var behind = Ac(1, -150, 0, 0, "Delta", "DAL1");          // 150 m behind, centre to centre: 93 m clear
        behind.AircraftType = "A320";
        h.Sim.Traffic.Add(behind);
        Roll(h, ref east, -3.0, 70);                               // a tug's 3 kt, tail-first

        string said = string.Join(" | ", h.Transcript);
        Assert.Contains(h.Said.All, m => m.StartsWith("Slow down, Delta A320 behind", StringComparison.Ordinal));
        string stop = Assert.Single(h.Said.All, m => m.StartsWith("Stop, Delta A320 very close, behind", StringComparison.Ordinal));
        // "Stop" is given for the 100 ft of gap it is given for nose-first, plus the lead 3 kt earns (35 ft).
        Assert.True(stop.EndsWith("125 feet.", StringComparison.Ordinal) || stop.EndsWith("150 feet.", StringComparison.Ordinal), said);
    }

    [Fact]
    public void Parked_the_aircraft_behind_draws_no_Stop_and_no_Slow_down()
    {
        var h = Stand(out double east);
        var behind = Ac(1, -60, 0, 0, "Delta", "DAL1");            // 3 m clear of the tail
        behind.AircraftType = "A320";
        h.Sim.Traffic.Add(behind);
        Roll(h, ref east, 0.0, 12);
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Stop", StringComparison.Ordinal) || m.StartsWith("Slow down", StringComparison.Ordinal));
    }

    [Fact]
    public void Pushed_back_away_from_the_aircraft_off_the_nose_it_is_not_Stop()
    {
        // Nose-in at a stand with an aircraft parked 20 m off the nose: backing away from it opens the gap.
        var h = Stand(out double east);
        var ahead = Ac(1, 34.0 + 20.3 + 20.0, 0, 0, "Delta", "DAL1");   // own nose 34.0, its tail 20.3, 20 m between
        ahead.AircraftType = "A320";
        h.Sim.Traffic.Add(ahead);
        Roll(h, ref east, 0.0, 4);
        h.Said.All.Clear();
        Roll(h, ref east, -3.0, 30);
        Assert.True(!h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal) || m.StartsWith("Slow down", StringComparison.Ordinal)), string.Join(" | ", h.Transcript));
    }

    [Fact]
    public void Rolling_forward_again_the_arc_is_ahead_of_the_nose_again()
    {
        var h = Stand(out double east);
        var ahead = Ac(1, 300, 0, 0, "Delta", "DAL1");
        ahead.AircraftType = "A320";
        h.Sim.Traffic.Add(ahead);
        Roll(h, ref east, -3.0, 20);                               // pushed back 31 m
        Roll(h, ref east, 0.0, 8);
        Roll(h, ref east, 10.0, 60);                               // then taxi out toward it
        Assert.Contains(h.Said.All, m => m.StartsWith("Stop, Delta A320 very close, ahead", StringComparison.Ordinal));
    }
}

public class GroundTrafficQueueWordingTests
{
    [Fact]
    public void Alone_on_a_route_the_summary_names_no_queue()
    {
        // OMDB, 2026-11-26: "1 aircraft nearby. ... crossing left to right. First in the queue." with nobody
        // ahead on the route: there was no queue, and "first" read as if the aircraft just named were.
        var h = new GroundTrafficHarness { Context = RouteContext(East(2000), 2000, departure: true) };
        h.Sim.Position = Own(0, 0);
        h.Sim.Traffic.Add(Ac(1, 0, -150, 0, "Delta", "DAL1"));       // beside the route, not on it
        h.Tick(6);
        string summary = h.Monitor.GetNearestTrafficSummary();
        Assert.DoesNotContain("queue", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("First", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Behind_another_the_summary_says_Number_2_and_at_its_head_afterwards_Number_1()
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(2000), 2000, departure: true) };
        h.Sim.Position = Own(0, 0);
        var leader = Ac(1, 300 / GroundTrafficLogic.FeetPerMetre, 0, 0, "Delta", "DAL1");
        h.Sim.Traffic.Add(leader);
        h.Tick(8);
        Assert.Contains(h.Said.All, m => m.StartsWith("Number 2 in the", StringComparison.Ordinal));
        Assert.Contains("Number 2 in the", h.Monitor.GetNearestTrafficSummary());

        h.Sim.Traffic.Clear();                                        // the leader has gone
        h.Tick(20);
        Assert.Contains(h.Said.All, m => m.StartsWith("Number 1 in the", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("First", StringComparison.Ordinal));
        Assert.Contains("Number 1 in the", h.Monitor.GetNearestTrafficSummary());
    }
}

public class GroundTrafficLiveQueueTests
{
    // OMDB, 2026-11-26, live: five aircraft queued on M for runway 30R, the pilot joins at the back, and
    // the line moves up one aircraft at a time at 8 kt while the one at the hold departs.
    private static (GroundTrafficHarness H, List<SimConnect.AiTrafficDataEventArgs> Line) Queue()
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), 1000, departure: true) };
        var line = new List<SimConnect.AiTrafficDataEventArgs>();
        double[] at = { 960, 850, 740, 625, 500 };                    // the head 40 m short of the route end
        for (int i = 0; i < at.Length; i++)
        {
            var ac = Ac((uint)(i + 1), at[i], 0, 0, "Delta", "DAL" + (i + 1));
            line.Add(ac);
            h.Sim.Traffic.Add(ac);
        }
        return (h, line);
    }

    [Fact]
    public void Joining_a_line_of_five_only_the_last_aircraft_is_called_on_the_route()
    {
        var (h, _) = Queue();
        double east = 0;
        for (int s = 0; s < 75; s++) { east += 12 * 0.514444; h.Sim.Position = Own(Math.Min(east, 330), east < 330 ? 12 : 0); h.Tick(); }
        string said = string.Join(" | ", h.Transcript);
        Assert.True(h.Said.All.Count(m => m.Contains("on your route", StringComparison.Ordinal)) == 1, said);
        Assert.True(h.Said.All.Any(m => m.StartsWith("Number 6 in the departure queue", StringComparison.Ordinal)), said);
    }

    [Fact]
    public void While_the_line_moves_up_the_pilot_s_number_does_not_fall()
    {
        var (h, line) = Queue();
        h.Sim.Position = Own(330, 0);
        h.Tick(10);
        Assert.Contains(h.Said.All, m => m.StartsWith("Number 6 in the departure queue", StringComparison.Ordinal));
        h.Said.All.Clear();

        // The head departs: it rolls onto the runway and away.
        h.Sim.Traffic.Remove(line[0]);
        h.Tick(6);
        // Then each aircraft moves up to the place ahead of it at 8 kt, one after the other.
        for (int i = 1; i < line.Count; i++)
        {
            var ac = line[i];
            double target = i == 1 ? 960 : line[i - 1].Longitude / M - 110;
            while (ac.Longitude / M < target)
            {
                ac.Longitude += 8 * 0.514444 * M;
                ac.GroundSpeedKnots = 8;
                h.Tick();
            }
            ac.GroundSpeedKnots = 0;
            h.Tick(3);
        }
        h.Tick(6);

        string said = string.Join(" | ", h.Transcript);
        // Four aircraft are ahead the whole time: "Number 5", once, and never 4, 3, 2 or 1.
        Assert.Contains(h.Said.All, m => m.StartsWith("Number 5 in the departure queue", StringComparison.Ordinal));
        Assert.True(!h.Said.All.Any(m => m.StartsWith("Number 4", StringComparison.Ordinal) || m.StartsWith("Number 3", StringComparison.Ordinal)
            || m.StartsWith("Number 2", StringComparison.Ordinal) || m.StartsWith("Number 1", StringComparison.Ordinal)), said);
        Assert.True(!h.Said.All.Any(m => m.Contains("in the queue", StringComparison.Ordinal)), said);
    }

    [Fact]
    public void An_aircraft_merely_taxiing_ahead_on_the_route_is_still_not_a_queue()
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), 3000, departure: true) };
        var leader = Ac(1, 200, 0, 12, "Delta", "DAL1");
        h.Sim.Traffic.Add(leader);
        double east = 0;
        for (int s = 0; s < 40; s++)
        {
            east += 12 * 0.514444; leader.Longitude += 12 * 0.514444 * M;
            h.Sim.Position = Own(east, 12);
            h.Tick();
        }
        Assert.DoesNotContain(h.Said.All, m => m.Contains("queue", StringComparison.Ordinal));
    }
}

public class GroundTrafficClearTests
{
    private const double Kt = 0.514444;

    [Fact]
    public void Stopped_for_crossing_traffic_the_pilot_is_told_when_it_has_gone()
    {
        // OMDB, 2026-11-26, live: "converging from the right", "Slow down", "Stop", "Stop" as an A320
        // crossed 15 m off the nose, and then nothing: the pilot had to ask whether it had gone.
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        var crosser = Ac(1, 10 * Kt * 40, -12 * Kt * 40, 12, "Delta", "DAL1", headingDeg: 0);
        h.Sim.Traffic.Add(crosser);
        double east = 0, north = -12 * Kt * 40, ownKts = 10;
        for (int s = 0; s < 90; s++)
        {
            if (h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal))) ownKts = 0;     // the pilot obeys
            east += ownKts * Kt; north += 12 * Kt;
            crosser.Latitude = north * M;
            h.Sim.Position = Own(east, ownKts);
            h.Tick();
        }
        string said = string.Join(" | ", h.Transcript);
        int stop = h.Said.All.FindLastIndex(m => m.StartsWith("Stop", StringComparison.Ordinal));
        int clear = h.Said.All.FindIndex(m => m == "Clear of Delta A320.");
        Assert.True(stop >= 0 && clear > stop, said);
        Assert.True(h.Said.All.Count(m => m.StartsWith("Clear of", StringComparison.Ordinal)) == 1, said);
    }

    [Fact]
    public void Stopped_early_behind_an_aircraft_that_stays_there_it_is_not_clear()
    {
        // At 30 kt "Stop" comes with 120 m to go. The pilot who brakes at once stops 90 m back, outside
        // the "Slow down" distance: the aircraft ahead is still ahead.
        var h = new GroundTrafficHarness { Context = RouteContext(East(3000), null, departure: false) };
        h.UseRealSizes("A320", 117.5);
        h.Sim.Traffic.Add(Ac(1, 900, 0, 0, "Delta", "DAL1"));
        double east = 0, ownKts = 30;
        for (int s = 0; s < 120; s++)
        {
            if (h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal))) ownKts = Math.Max(0, ownKts - 12);
            east += ownKts * Kt;
            h.Sim.Position = Own(east, ownKts);
            h.Tick();
        }
        Assert.Contains(h.Said.All, m => m.StartsWith("Stop", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Clear of", StringComparison.Ordinal));
    }

    [Fact]
    public void Pushed_back_toward_an_aircraft_that_then_taxies_away_the_pilot_hears_it_is_clear()
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.UseRealSizes("B77W", 212.6);
        var behind = Ac(1, -150, 0, 0, "Delta", "DAL1", headingDeg: 0);     // across the taxilane behind the stand
        behind.AircraftType = "A320";
        h.Sim.Traffic.Add(behind);
        double east = 0, north = 0, ownKts = -3;
        for (int s = 0; s < 140; s++)
        {
            bool stopped = h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal));
            if (stopped) ownKts = 0;
            east += ownKts * Kt;
            if (stopped && s > 70) { north += 12 * Kt; behind.Latitude = north * M; behind.GroundSpeedKnots = 12; }   // it taxies on
            h.Sim.Position = Own(east, Math.Abs(ownKts));
            h.Tick();
        }
        string said = string.Join(" | ", h.Transcript);
        Assert.True(h.Said.All.Any(m => m.StartsWith("Stop, Delta A320 very close, behind", StringComparison.Ordinal)), said);
        Assert.True(h.Said.All.Any(m => m == "Clear of Delta A320."), said);
    }

    [Fact]
    public void Stopped_after_a_pushback_the_aircraft_behind_is_not_clear_until_it_has_gone()
    {
        // OMDB, 2026-11-26, live: "Stop, 777 very close, behind, 125 feet", the pushback stopped 35 m short,
        // and six seconds later "Clear of 777" with the 777 still there. Stopped, the aircraft was read as
        // facing forward again, and what was behind it was no longer "in the way".
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), 1000, departure: true) };
        h.UseRealSizes("B77W", 212.6);
        var behind = Ac(1, -150, 0, 0, "Delta", "DAL1", headingDeg: 0);
        behind.AircraftType = "A320";
        h.Sim.Traffic.Add(behind);
        double east = 0, north = 0, ownKts = -3;
        int stopSecond = -1, clearSecond = -1;
        for (int s = 0; s < 200; s++)
        {
            if (stopSecond < 0 && h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal))) { stopSecond = s; ownKts = 0; }
            east += ownKts * Kt;
            if (s > 150) { north += 12 * Kt; behind.Latitude = north * M; behind.GroundSpeedKnots = 12; }   // it taxies on at last
            h.Sim.Position = Own(east, Math.Abs(ownKts));
            h.Tick();
            if (clearSecond < 0 && h.Said.All.Any(m => m.StartsWith("Clear of", StringComparison.Ordinal))) clearSecond = s;
        }
        string said = string.Join(" | ", h.Transcript);
        Assert.True(stopSecond > 0, said);
        Assert.True(clearSecond > 150, $"clear at {clearSecond}: {said}");
        Assert.True(h.Said.All.Count(m => m.StartsWith("Stop", StringComparison.Ordinal)) == 1, said);
        // And nothing about a queue while the aircraft is pushed back or stands where the push left it.
        Assert.True(!h.Said.All.Any(m => m.Contains("queue", StringComparison.Ordinal)), said);
    }

    [Fact]
    public void Taxiing_out_after_the_pushback_the_aircraft_left_behind_is_not_announced_clear()
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.UseRealSizes("B77W", 212.6);
        var behind = Ac(1, -150, 0, 0, "Delta", "DAL1", headingDeg: 0);
        behind.AircraftType = "A320";
        h.Sim.Traffic.Add(behind);
        double east = 0, ownKts = -3;
        for (int s = 0; s < 150; s++)
        {
            if (ownKts < 0 && h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal))) ownKts = 0;
            if (s > 100) ownKts = 10;                                   // the tug is gone: taxi out, away from it
            east += ownKts * Kt;
            h.Sim.Position = Own(east, Math.Abs(ownKts));
            h.Tick();
        }
        string said = string.Join(" | ", h.Transcript);
        Assert.True(h.Said.All.Any(m => m.StartsWith("Stop, Delta A320 very close, behind", StringComparison.Ordinal)), said);
        Assert.True(!h.Said.All.Any(m => m.StartsWith("Clear of", StringComparison.Ordinal)), said);
    }

    [Fact]
    public void The_aircraft_ahead_in_a_line_is_answered_by_ahead_is_moving_alone()
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(2000), 2000, departure: true) };
        h.UseRealSizes("A320", 117.5);
        var leader = Ac(1, 600, 0, 0, "Delta", "DAL1");
        h.Sim.Traffic.Add(leader);
        double east = 0, lead = 600, ownKts = 12;
        for (int s = 0; s < 160; s++)
        {
            if (h.Said.All.Any(m => m.StartsWith("Stop", StringComparison.Ordinal))) ownKts = 0;
            east += ownKts * Kt;
            if (s > 90) { lead += 10 * Kt; leader.Longitude = lead * M; leader.GroundSpeedKnots = 10; }
            h.Sim.Position = Own(east, ownKts);
            h.Tick();
        }
        string said = string.Join(" | ", h.Transcript);
        Assert.True(h.Said.All.Any(m => m == "Delta A320 ahead is moving."), said);
        Assert.True(!h.Said.All.Any(m => m.StartsWith("Clear of", StringComparison.Ordinal)), said);
    }
}

public class GroundTrafficLongQueueTests
{
    [Theory]
    [InlineData(20, 70.0, "Number 21 in the departure queue.")]     // 1,400 m of aircraft
    [InlineData(25, 70.0, "Number 26 in the departure queue.")]     // 1,750 m: a Cross the Pond evening
    [InlineData(35, 80.0, "Number 36 in the departure queue.")]     // 2,800 m, as far as the queue looks
    [InlineData(20, 70.0, "Number 21 in the queue.")]               // a line on the way, kilometres short of the runway
    public void A_line_of_twenty_and_more_is_counted(int aircraft, double spacingM, string expected)
    {
        bool atHold = expected.Contains("departure", StringComparison.Ordinal);
        double firstAt = 100, last = firstAt + (aircraft - 1) * spacingM;
        // At the hold the route ends 40 m beyond the head of the line; otherwise it runs on for kilometres.
        double end = atHold ? last + 40 : last + 4000;
        var h = new GroundTrafficHarness { Context = RouteContext(East(end), end, departure: true) };
        for (int i = 0; i < aircraft; i++) h.Sim.Traffic.Add(Ac((uint)(i + 1), firstAt + i * spacingM, 0, 0, "Delta", "DAL" + i));
        h.Sim.Position = Own(0, 0);
        h.Tick(12);
        Assert.True(h.Said.All.Contains(expected), string.Join(" | ", h.Transcript));
    }
}
