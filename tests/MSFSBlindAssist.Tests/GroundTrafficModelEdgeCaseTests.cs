// The corners of measuring an installed aircraft model ([TRF-12]): how MSFS packs a mesh's materials into
// shared buffers, the helper geometry and lights an add-on hangs on its airframe, which level of detail is
// fit to measure, an MSFS 2024 MODULAR aircraft assembled from attachments that live in several packages,
// model templates and merges, an FSX-era .MDL, a package installed while the app runs, and the own
// aircraft's outline held against its type's size. Every file here is synthetic.

using System.Globalization;
using System.Text;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Writes small glTF models: boxes seen from above, on named nodes.</summary>
internal static class TestModel
{
    /// <summary>What a box is drawn with: an ordinary material, the simulator's invisible one, or as an environment occluder.</summary>
    /// <remarks>Decal, Transparent and Glass are see-through (alphaMode BLEND): a picture on the surface, a material of alpha 0, and one that is drawn (glass, a rotor disc).</remarks>
    /// <remarks>Blur is see-through AND textured: the card a turning rotor or propeller is drawn on.</remarks>
    public enum Material { Visible, Invisible, Occluder, InvisibleSwitchedOff, Decal, Transparent, Glass, Blur }

    /// <summary>A flat box: x from XShift - HalfWidth to XShift + HalfWidth, z from ZMin to ZMax.</summary>
    public sealed record Box(double HalfWidth, double ZMin, double ZMax, double XShift = 0, Material Material = Material.Visible);

    /// <summary>How a node's boxes share buffers: each its own, or one vertex and one index accessor the way MSFS packs materials.</summary>
    public enum Packing { Separate, StartIndex, BaseVertex }

    /// <param name="UniqueId">Its <c>ASOBO_unique_id</c>, when it has one.</param>
    /// <param name="Scene">The scene a root node belongs to (0 is the default scene).</param>
    /// <param name="Skeleton">For a skinned mesh, the node its skin names as its skeleton (else its skin names node 0 as its one joint).</param>
    /// <param name="TurnedDeg">Turned this far about the vertical axis.</param>
    public sealed record Node(string Name, Box[]? Boxes = null, double[]? Translation = null, int[]? Children = null,
        bool Skinned = false, string? AnimatedBy = null, Packing Packing = Packing.Separate, string? UniqueId = null, int Scene = 0,
        int? Skeleton = null, double TurnedDeg = 0);

    public static readonly Box Fuselage = new(2, -22, 18);
    public static readonly Box Wing = new(17, -4, 2);

    public static string Write(string dir, string name, params Node[] nodes) => Write(dir, name, null, null, nodes);

    public static string Write(string dir, string name, long? declaredBytes, params Node[] nodes) => Write(dir, name, declaredBytes, null, nodes);

    /// <param name="declaredBytes">What the file says its geometry weighs, when not what it does.</param>
    /// <param name="sceneTargets">Per scene, the unique id of the node it hangs on (null: none).</param>
    public static string Write(string dir, string name, long? declaredBytes, string?[]? sceneTargets, params Node[] nodes)
    {
        Directory.CreateDirectory(dir);
        var bin = new List<byte>();
        var views = new List<string>();
        var accessors = new List<string>();
        var meshes = new List<string>();
        var nodeJson = new List<string>();
        var animations = new List<string>();
        var skeletons = new List<int>();

        int AddVertices(IEnumerable<Box> boxes)
        {
            int offset = bin.Count, count = 0;
            foreach (var b in boxes)
            {
                float[] v =
                {
                    (float)(b.XShift - b.HalfWidth), 0, (float)b.ZMin, (float)(b.XShift + b.HalfWidth), 0, (float)b.ZMin,
                    (float)(b.XShift + b.HalfWidth), 0, (float)b.ZMax, (float)(b.XShift - b.HalfWidth), 0, (float)b.ZMax,
                };
                foreach (float f in v) bin.AddRange(BitConverter.GetBytes(f));
                count += 4;
            }
            views.Add(Inv($"{{\"buffer\":0,\"byteOffset\":{offset},\"byteLength\":{count * 12}}}"));
            accessors.Add(Inv($"{{\"bufferView\":{views.Count - 1},\"componentType\":5126,\"count\":{count},\"type\":\"VEC3\"}}"));
            return accessors.Count - 1;
        }

        int AddIndices(IEnumerable<ushort> indices)
        {
            int offset = bin.Count, count = 0;
            foreach (ushort ix in indices) { bin.AddRange(BitConverter.GetBytes(ix)); count++; }
            views.Add(Inv($"{{\"buffer\":0,\"byteOffset\":{offset},\"byteLength\":{count * 2}}}"));
            accessors.Add(Inv($"{{\"bufferView\":{views.Count - 1},\"componentType\":5123,\"count\":{count},\"type\":\"SCALAR\"}}"));
            return accessors.Count - 1;
        }

        ushort[] quad = { 0, 1, 2, 0, 2, 3 };
        for (int n = 0; n < nodes.Length; n++)
        {
            var node = nodes[n];
            var json = new StringBuilder(Inv($"{{\"name\":\"{node.Name}\""));
            if (node.Boxes is { Length: > 0 } boxes)
            {
                var primitives = new List<string>();
                if (node.Packing == Packing.Separate)
                {
                    foreach (var box in boxes)
                    {
                        int position = AddVertices(new[] { box }), indices = AddIndices(quad);
                        primitives.Add(Inv($"{{\"attributes\":{{\"POSITION\":{position}}},\"indices\":{indices},\"material\":{(int)box.Material}}}"));
                    }
                }
                else
                {
                    bool relative = node.Packing == Packing.BaseVertex;
                    int position = AddVertices(boxes);
                    int indices = AddIndices(Enumerable.Range(0, boxes.Length)
                        .SelectMany(b => quad.Select(q => (ushort)(q + (relative ? 0 : b * 4)))));
                    for (int b = 0; b < boxes.Length; b++)
                    {
                        string slice = "\"PrimitiveCount\":2" + (b > 0 ? Inv($",\"StartIndex\":{b * 6}") : "")
                            + (relative ? Inv($",\"VertexCount\":4") + (b > 0 ? Inv($",\"BaseVertexIndex\":{b * 4}") : "") : "");
                        primitives.Add(Inv($"{{\"attributes\":{{\"POSITION\":{position}}},\"indices\":{indices},\"material\":{(int)boxes[b].Material},\"extras\":{{\"ASOBO_primitive\":{{{slice}}}}}}}"));
                    }
                }
                meshes.Add("{\"primitives\":[" + string.Join(",", primitives) + "]}");
                json.Append(Inv($",\"mesh\":{meshes.Count - 1}"));
            }
            if (node.Translation is { } t) json.Append(Inv($",\"translation\":[{t[0]},{t[1]},{t[2]}]"));
            if (node.Children is { Length: > 0 } kids) json.Append(",\"children\":[" + string.Join(",", kids) + "]");
            if (node.TurnedDeg != 0)
            {
                double half = node.TurnedDeg * Math.PI / 360.0;
                json.Append(Inv($",\"rotation\":[0,{Math.Sin(half)},0,{Math.Cos(half)}]"));
            }
            if (node.Skeleton is { } bone)
            {
                if (!skeletons.Contains(bone)) skeletons.Add(bone);
                json.Append(Inv($",\"skin\":{skeletons.IndexOf(bone) + 1}"));
            }
            else if (node.Skinned) json.Append(",\"skin\":0");
            if (node.UniqueId != null) json.Append(Inv($",\"extensions\":{{\"ASOBO_unique_id\":{{\"id\":\"{node.UniqueId}\"}}}}"));
            json.Append('}');
            nodeJson.Add(json.ToString());
            if (node.AnimatedBy != null)
                animations.Add(Inv($"{{\"name\":\"{node.AnimatedBy}\",\"channels\":[{{\"sampler\":0,\"target\":{{\"node\":{n},\"path\":\"weights\"}}}}],\"samplers\":[{{\"input\":0,\"output\":0}}]}}"));
        }

        var children = nodes.SelectMany(n => n.Children ?? Array.Empty<int>()).ToHashSet();
        int sceneCount = Math.Max(sceneTargets?.Length ?? 1, nodes.Max(n => n.Scene) + 1);
        var scenes = new List<string>();
        for (int s = 0; s < sceneCount; s++)
        {
            string roots = string.Join(",", Enumerable.Range(0, nodes.Length).Where(i => !children.Contains(i) && nodes[i].Scene == s));
            string target = sceneTargets != null && s < sceneTargets.Length && sceneTargets[s] != null
                ? Inv($",\"extensions\":{{\"ASOBO_unique_id\":{{\"id\":\"{sceneTargets[s]}\"}}}}") : "";
            scenes.Add("{\"nodes\":[" + roots + "]" + target + "}");
        }
        string gltf = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[" + string.Join(",", scenes) + "],\"nodes\":[" + string.Join(",", nodeJson)
            + "],\"materials\":[{\"name\":\"Paint\"},{\"name\":\"Inv Col\",\"extensions\":{\"ASOBO_material_invisible\":{}}},{\"name\":\"Occluder\",\"extensions\":{\"ASOBO_material_environment_occluder\":{}}},{\"name\":\"Shown\",\"extensions\":{\"ASOBO_material_invisible\":{\"enabled\":false}}}"
            + ",{\"name\":\"Decal\",\"alphaMode\":\"BLEND\",\"extensions\":{\"ASOBO_material_geometry_decal\":{}}}"
            + ",{\"name\":\"Nothing\",\"alphaMode\":\"BLEND\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[1.0,1.0,1.0,0.0]}}"
            + ",{\"name\":\"Glass\",\"alphaMode\":\"BLEND\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[1.0,1.0,1.0,0.3]},\"extensions\":{\"ASOBO_material_shadow_options\":{\"noCastShadow\":true}}}"
            + ",{\"name\":\"Blur\",\"alphaMode\":\"BLEND\",\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}]"
            + ",\"meshes\":[" + string.Join(",", meshes) + "],\"accessors\":[" + string.Join(",", accessors)
            + "],\"bufferViews\":[" + string.Join(",", views)
            + "],\"buffers\":[{\"uri\":\"" + name + ".bin\",\"byteLength\":" + (declaredBytes ?? bin.Count).ToString(CultureInfo.InvariantCulture) + "}]"
            + (animations.Count > 0 ? ",\"animations\":[" + string.Join(",", animations) + "]" : "")
            + (nodes.Any(n => n.Skinned || n.Skeleton != null)
                ? ",\"skins\":[{\"joints\":[0]}" + string.Concat(skeletons.Select(s => Inv($",{{\"joints\":[{s}],\"skeleton\":{s}}}"))) + "]"
                : "") + "}";
        File.WriteAllBytes(Path.Combine(dir, name + ".bin"), bin.ToArray());
        string path = Path.Combine(dir, name + ".gltf");
        File.WriteAllText(path, gltf);
        return path;
    }

    private static string Inv(FormattableString s) => FormattableString.Invariant(s);

    /// <summary>
    /// An FSX-format model: RIFF "MDLX" holding the vertices (32 bytes each, position first), the indices,
    /// and two levels of detail: a three-vertex stand-in listed first, then one part per box.
    /// </summary>
    public static string WriteLegacy(string dir, string name, params Box[] boxes)
    {
        Directory.CreateDirectory(dir);
        static byte[] Chunk(string id, params byte[][] content)
        {
            var body = content.SelectMany(c => c).ToList();
            if (body.Count % 2 == 1) body.Add(0);
            return Encoding.ASCII.GetBytes(id).Concat(BitConverter.GetBytes(body.Count)).Concat(body).ToArray();
        }
        static byte[] PartRecord(int vertexOffset, int vertexCount, int indexOffset, int indexCount)
        {
            var record = new byte[36];
            BitConverter.GetBytes(1).CopyTo(record, 0);              // a triangle list
            BitConverter.GetBytes(vertexOffset).CopyTo(record, 16);
            BitConverter.GetBytes(vertexCount).CopyTo(record, 20);
            BitConverter.GetBytes(indexOffset).CopyTo(record, 24);
            BitConverter.GetBytes(indexCount).CopyTo(record, 28);
            return record;
        }

        var vertices = new List<byte>();
        var indices = new List<byte>();
        var detailed = new List<byte[]>();
        for (int b = 0; b < boxes.Length; b++)
        {
            var box = boxes[b];
            (double X, double Z)[] corners =
            {
                (box.XShift - box.HalfWidth, box.ZMin), (box.XShift + box.HalfWidth, box.ZMin),
                (box.XShift + box.HalfWidth, box.ZMax), (box.XShift - box.HalfWidth, box.ZMax),
            };
            foreach (var (x, z) in corners)
            {
                vertices.AddRange(BitConverter.GetBytes((float)x));
                vertices.AddRange(BitConverter.GetBytes(0f));
                vertices.AddRange(BitConverter.GetBytes((float)z));
                vertices.AddRange(new byte[20]);
            }
            foreach (ushort ix in new ushort[] { 0, 1, 2, 0, 2, 3 }) indices.AddRange(BitConverter.GetBytes(ix));
            detailed.Add(Chunk("PART", PartRecord(b * 4, 4, b * 6, 6)));
        }
        byte[] coarse = Chunk("LODE", BitConverter.GetBytes(5), Chunk("PART", PartRecord(0, 3, 0, 3)));
        byte[] fine = Chunk("LODE", new[] { BitConverter.GetBytes(400) }.Concat(detailed).ToArray());
        byte[] data = Chunk("MDLD", Chunk("VERT", vertices.ToArray()), Chunk("INDE", indices.ToArray()), Chunk("LODT", coarse, fine));
        byte[] file = Encoding.ASCII.GetBytes("RIFF").Concat(BitConverter.GetBytes(data.Length + 4))
            .Concat(Encoding.ASCII.GetBytes("MDLX")).Concat(data).ToArray();
        string path = Path.Combine(dir, name + ".mdl");
        File.WriteAllBytes(path, file);
        return path;
    }
}

public class GroundTrafficModelEdgeCaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-model-edge-" + Guid.NewGuid().ToString("N"));

    public GroundTrafficModelEdgeCaseTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    private static TestModel.Node Body(string name, params TestModel.Box[] boxes) => new(name, boxes);
    private static readonly TestModel.Box Fuselage = TestModel.Fuselage, Wing = TestModel.Wing;

    // ── Materials packed into shared buffers ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]   // the MSFS 2020 exporter (AIG, FSLTL, IVAO): StartIndex, indices from vertex 0
    [InlineData(true)]    // the MSFS 2024 exporter (iniBuilds, PMDG, Fenix): BaseVertexIndex as well
    public void A_material_s_slice_of_a_shared_buffer_is_drawn_with_its_own_vertices(bool baseVertex)
    {
        var packing = baseVertex ? TestModel.Packing.BaseVertex : TestModel.Packing.StartIndex;
        // The fuselage is the first material, the wing the second. Read without the slice's BaseVertexIndex
        // the wing's triangles would join the fuselage's vertices: a model 4 m wide.
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "packed-" + packing,
            new TestModel.Node("airframe", new[] { Fuselage, Wing }, Packing: packing)))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);
        Assert.Equal(22.0, outline.TailM, 2);
        // The wing is where it was drawn, not spread over the fuselage's length.
        Assert.Equal(2.0, outline.HalfWidths[0], 2);
        Assert.Equal(2.0, outline.HalfWidths[^1], 2);
    }

    // ── Helper geometry, lights, shadows ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("A350_Wing_Root")]        // under any name: four vertices 100 m across and along
    [InlineData("x0_plane")]
    public void A_handful_of_vertices_spread_wide_both_ways_is_a_helper_whatever_it_is_called(string name)
    {
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "helper", Body("fuselage", Fuselage), Body("wing", Wing),
            Body(name, new TestModel.Box(50, -50, 50))))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);
        Assert.Equal(22.0, outline.TailM, 2);
    }

    [Fact]
    public void A_helper_packed_into_the_airframe_s_own_buffer_is_dropped_alone()
    {
        // The helper plane is the wing mesh's second material: its slice goes, the wing stays.
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "helper-packed", Body("fuselage", Fuselage),
            new TestModel.Node("wing", new[] { Wing, new TestModel.Box(50, -50, 50) }, Packing: TestModel.Packing.BaseVertex)))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(22.0, outline.TailM, 2);
    }

    [Fact]
    public void A_wide_part_of_many_vertices_and_a_long_thin_one_are_airframe()
    {
        // A wing is wide but never 40 m along as well; a fuselage is long but never 40 m across.
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "big",
            Body("fuselage", new TestModel.Box(3, -38, 35)), Body("wing", new TestModel.Box(40, -10, 5))))!;
        Assert.Equal(40.0, outline.HalfSpanM, 2);
        Assert.Equal(73.0, outline.LengthM, 2);
    }

    [Theory]
    [InlineData("LIGHT_left_strobe", TestModel.Material.Invisible)]       // the PMDG 777's 31 m sphere round each lamp
    [InlineData("A359_EXT_WING_LEFT", TestModel.Material.Invisible)]      // the iniBuilds A350's 35 m box on each wing
    [InlineData("Skel_Fuselage_BoxLOD", TestModel.Material.Invisible)]    // the Fenix's box round its fuselage
    [InlineData("DRONE_COLLISION_IN", TestModel.Material.Invisible)]
    [InlineData("OCCLUDER_1000", TestModel.Material.Occluder)]
    public void What_is_drawn_with_the_invisible_material_or_as_an_occluder_is_not_airframe(string name, object material)
    {
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "unseen", Body("fuselage", Fuselage), Body("wing", Wing),
            Body(name, new TestModel.Box(30, 10, 30, Material: (TestModel.Material)material))))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);
    }

    [Theory]
    [InlineData("l_ldg_light_src", TestModel.Material.Transparent)]     // the TFDi MD-11's landing-light beam: alpha 0
    [InlineData("nav_r_fwd", TestModel.Material.Decal)]                 // and its wingtip lights' glow, 12 m across
    public void What_is_drawn_see_through_as_a_decal_or_with_no_colour_at_all_is_not_airframe(string name, object material)
    {
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "light", Body("fuselage", Fuselage), Body("wing", Wing),
            Body(name, new TestModel.Box(30, 10, 30, Material: (TestModel.Material)material))))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);
    }

    [Fact]
    public void A_see_through_material_that_is_drawn_is_airframe()
    {
        // A helicopter's rotor disc: see-through, casting no shadow, and the widest thing it has.
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "rotor", Body("fuselage", new TestModel.Box(1, -6, 4)),
            Body("rotor_disc", new TestModel.Box(5, -5, 5, Material: TestModel.Material.Glass))))!;
        Assert.Equal(5.0, outline.HalfSpanM, 2);
    }

    [Fact]
    public void An_invisible_material_that_is_switched_off_is_seen()
    {
        // "ASOBO_material_invisible": { "enabled": false } is an ordinary material.
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "seen", Body("fuselage", Fuselage),
            Body("wing", new TestModel.Box(17, -4, 2, Material: TestModel.Material.InvisibleSwitchedOff))))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
    }

    [Fact]
    public void An_invisible_slice_of_the_wing_s_own_buffer_goes_and_the_wing_stays()
    {
        // The A350's wing: its paint and its invisible 35 m box are two materials of ONE mesh.
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "unseen-packed", Body("fuselage", Fuselage),
            new TestModel.Node("A359_EXT_WING_LEFT", new[] { Wing, new TestModel.Box(35, -20, 15, Material: TestModel.Material.Invisible) },
                Packing: TestModel.Packing.BaseVertex)))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(22.0, outline.TailM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);
    }

    [Theory]
    [InlineData("GEAR_GSE")]
    [InlineData("Fuel_Truck")]
    [InlineData("TowBar")]
    [InlineData("WheelChocks_L")]
    [InlineData("tail_drogue_line5")]
    public void Ground_equipment_is_named_and_is_not_the_airframe(string name)
    {
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "around", Body("fuselage", Fuselage), Body("wing", Wing),
            Body(name, new TestModel.Box(30, 10, 30))))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);
    }

    [Theory]
    [InlineData("cabin_lights_VC")]              // an FSX-converted model's whole airframe (353 of AIG's)
    [InlineData("LIGHT_Nav_green")]              // AIG's 787: the wingtip itself
    [InlineData("custom_vis_LIGHT_LANDING_0_mcx_VC")]
    [InlineData("Light_Landing_Wing_R")]         // the Fenix's lamp housings
    [InlineData("LOD_friend_wing")]              // a name is no test: what is seen is airframe
    [InlineData("CastShadows_Fuselage")]
    [InlineData("skel_wing_box")]
    [InlineData("tail_cone")]                    // "cone" once dropped real tail cones
    [InlineData("Flight_Deck_Windows")]
    [InlineData("stugeon")]                      // "tug" inside a word
    public void A_visible_part_is_airframe_whatever_it_is_called(string name)
    {
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "kept", Body("fuselage", Fuselage), Body("wing", Wing),
            Body(name, new TestModel.Box(1, -27, -21))))!;
        Assert.Equal(27.0, outline.TailM, 2);
    }

    [Fact]
    public void Everything_under_a_part_that_is_not_airframe_goes_with_it()
    {
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "under", Body("fuselage", Fuselage), Body("wing", Wing),
            new TestModel.Node("GroundService", Children: new[] { 3 }), Body("truck_body", new TestModel.Box(3, 15, 30))))!;
        Assert.Equal(18.0, outline.NoseM, 2);
    }

    // ── Which level of detail ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_coarse_level_is_never_preferred_to_a_detailed_one()
    {
        // Three detailed levels with flexing (skinned) wings, then a fourth with none and no tail cone:
        // the PMDG 777's sixth level is that. The first is measured.
        string Detailed(string n) => TestModel.Write(_dir, n, Body("fuselage", Fuselage), new TestModel.Node("wing", new[] { Wing }, Skinned: true));
        string coarse = TestModel.Write(_dir, "lod3", Body("fuselage", new TestModel.Box(2, -15, 18)), Body("wing", Wing));
        var outline = GroundTrafficModelReader.Measure(new[] { Detailed("lod0"), Detailed("lod1"), Detailed("lod2"), coarse })!;
        Assert.Equal(22.0, outline.TailM, 2);
    }

    [Fact]
    public void A_coarse_level_is_measured_when_no_detailed_one_can_be_read()
    {
        string broken = Path.Combine(_dir, "broken.gltf");
        File.WriteAllText(broken, "{ not a model");
        string coarse = TestModel.Write(_dir, "only", Body("fuselage", Fuselage), Body("wing", Wing));
        Assert.Equal(17.0, GroundTrafficModelReader.Measure(new[] { broken, broken, broken, coarse })!.HalfSpanM, 2);
    }

    [Fact]
    public void A_level_that_weighs_too_much_is_passed_over_for_a_lighter_one()
    {
        // Sixty megabytes, by its own word: the next level gives the same outline at a fraction of the read.
        string heavy = TestModel.Write(_dir, "heavy", 60L * 1024 * 1024, Body("fuselage", Fuselage), Body("wing", new TestModel.Box(25, -4, 2)));
        string light = TestModel.Write(_dir, "light", Body("fuselage", Fuselage), Body("wing", Wing));
        Assert.Equal(17.0, GroundTrafficModelReader.Measure(new[] { heavy, light })!.HalfSpanM, 2);
        // With nothing lighter it is still measured.
        Assert.Equal(25.0, GroundTrafficModelReader.Measure(new[] { heavy })!.HalfSpanM, 2);
    }

    [Fact]
    public void A_model_saved_with_a_byte_order_mark_is_read()
    {
        string path = TestModel.Write(_dir, "bom", Body("fuselage", Fuselage), Body("wing", Wing));
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(File.ReadAllBytes(path)).ToArray());
        Assert.Equal(17.0, GroundTrafficModelReader.Measure(path)!.HalfSpanM, 2);
    }

    // ── A model assembled from parts ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_part_is_placed_at_the_node_of_its_parent_that_it_is_attached_to()
    {
        string fuselage = TestModel.Write(_dir, "fuselage", Body("fuselage", Fuselage),
            new TestModel.Node("Attach_Wings", Translation: new[] { 0.0, 0.0, -10.0 }));
        string wings = TestModel.Write(_dir, "wings", Body("wing", Wing));
        GroundTrafficModelReader.ModelPart[] parts =
        {
            new(new[] { fuselage }), new(new[] { wings }, Parent: 0, AttachNode: "attach_wings"),
        };
        var outline = GroundTrafficModelReader.Measure(parts)!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        // The wing spans z -14 to -8 in the fuselage's frame.
        int widest = Enumerable.Range(0, outline.HalfWidths.Count).First(i => outline.HalfWidths[i] > 16.9f);
        Assert.Equal(-14.0, -outline.TailM + widest * AircraftOutline.StationM, 0);
    }

    [Fact]
    public void A_part_attached_to_a_part_that_is_itself_attached_carries_both_offsets()
    {
        string common = TestModel.Write(_dir, "common", new TestModel.Node("Attach_Fuselage", Translation: new[] { 0.0, 0.0, 5.0 }));
        string fuselage = TestModel.Write(_dir, "fuselage2", Body("fuselage", Fuselage),
            new TestModel.Node("Attach_Wings", Translation: new[] { 0.0, 0.0, -10.0 }));
        string wings = TestModel.Write(_dir, "wings2", Body("wing", Wing));
        GroundTrafficModelReader.ModelPart[] parts =
        {
            new(new[] { common }),                                                  // no airframe of its own
            new(new[] { fuselage }, Parent: 0, AttachNode: "Attach_Fuselage"),
            new(new[] { wings }, Parent: 1, AttachNode: "Attach_Wings"),
        };
        var outline = GroundTrafficModelReader.Measure(parts)!;
        Assert.Equal(23.0, outline.NoseM, 2);
        Assert.Equal(17.0, outline.TailM, 2);
        int widest = Enumerable.Range(0, outline.HalfWidths.Count).First(i => outline.HalfWidths[i] > 16.9f);
        Assert.Equal(-9.0, -outline.TailM + widest * AircraftOutline.StationM, 0);
    }

    [Fact]
    public void A_part_whose_attach_node_or_whose_parent_is_missing_sits_at_its_parent_s_origin()
    {
        string fuselage = TestModel.Write(_dir, "fuselage3", Body("fuselage", Fuselage));
        string wings = TestModel.Write(_dir, "wings3", Body("wing", Wing));
        var unknownNode = GroundTrafficModelReader.Measure(new GroundTrafficModelReader.ModelPart[]
        {
            new(new[] { fuselage }), new(new[] { wings }, Parent: 0, AttachNode: "No_Such_Node"),
        })!;
        Assert.Equal(17.0, unknownNode.HalfSpanM, 2);
        var unreadableParent = GroundTrafficModelReader.Measure(new GroundTrafficModelReader.ModelPart[]
        {
            new(new[] { Path.Combine(_dir, "gone.gltf") }), new(new[] { fuselage }, Parent: 0, AttachNode: "x"),
            new(new[] { wings }, Parent: 0),
        })!;
        Assert.Equal(17.0, unreadableParent.HalfSpanM, 2);
        Assert.Equal(18.0, unreadableParent.NoseM, 2);
    }

    [Fact]
    public void A_part_goes_on_an_attach_node_kept_in_a_model_merged_into_its_parent()
    {
        // The PMDG 737: its fuselage carries the tail unit on ATTACH_POINT_738_TAIL, and that node is not in
        // the fuselage's own file but in a ROOT.gltf merged into it, read as a part of its own. Looked for in
        // the fuselage alone it was not found: fin, tailplane and elevators were drawn at the origin, inside
        // the wing, and every 737 measured 1.43 m short with no tailplane (38.04 m for a 737-800 of 39.47).
        string fuselage = TestModel.Write(_dir, "fuselage738", Body("fuselage", Fuselage));
        string points = TestModel.Write(_dir, "ROOT", new TestModel.Node("ATTACH_POINT_TAIL", Translation: new[] { 0.0, 0.0, -18.0 }));
        string tail = TestModel.Write(_dir, "tail738", Body("h-stab", new TestModel.Box(7, -5.5, 0)));
        var outline = GroundTrafficModelReader.Measure(new GroundTrafficModelReader.ModelPart[]
        {
            new(new[] { fuselage }), new(new[] { points }, Parent: 0), new(new[] { tail }, Parent: 0, AttachNode: "ATTACH_POINT_TAIL"),
        })!;

        Assert.Equal(23.5, outline.TailM, 2);            // the tailplane's tips, 1.5 m behind the tail cone at 22
        Assert.Equal(7.0, outline.HalfSpanM, 2);
        int atTheTail = (int)Math.Round((outline.TailM - 20.0) / AircraftOutline.StationM);
        Assert.Equal(7.0, outline.HalfWidths[atTheTail], 1);       // and it is as wide as the tailplane there
    }

    // ── Skinned meshes ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_skinned_mesh_is_placed_in_the_frame_its_skeleton_stands_in()
    {
        // The PMDG 737: each wing's skin is stored from the wing root outwards, and its bones hang under a
        // WING_LEFT node 2 m out from the centreline. The mesh's own node says something else and is not
        // applied. Read as stored the wing reached 15 m; it reaches 17.
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "skinned-wing", Body("fuselage", Fuselage),
            new TestModel.Node("WING_LEFT", Translation: new[] { 2.0, 0.0, 0.0 }, Children: new[] { 2 }),
            new TestModel.Node("wing_bone1_left"),
            new TestModel.Node("WING_RIGHT", Translation: new[] { -2.0, 0.0, 0.0 }, Children: new[] { 4 }),
            new TestModel.Node("wing_bone1_right"),
            new TestModel.Node("WING_LEFT_1", new[] { new TestModel.Box(7.5, -4, 2, XShift: 7.5) }, Translation: new[] { 30.0, 0.0, 9.0 }, Skeleton: 2),
            new TestModel.Node("WING_RIGHT_1", new[] { new TestModel.Box(7.5, -4, 2, XShift: -7.5) }, Translation: new[] { 30.0, 0.0, 9.0 }, Skeleton: 4)))!;

        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);            // and nothing was moved 9 m ahead by the mesh's own node
    }

    [Fact]
    public void A_skinned_mesh_whose_skeleton_hangs_under_the_landing_gear_is_gear_too()
    {
        // A strut's cables and springs are skinned to bones under the gear: placed there they would stand
        // ahead of the nose with the gear down, and gear never shapes the outline.
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "skinned-gear", Body("fuselage", Fuselage), Body("wing", Wing),
            new TestModel.Node("nose_gear", Translation: new[] { 0.0, 0.0, 22.0 }, Children: new[] { 3 }, AnimatedBy: "c_gear"),
            new TestModel.Node("scissor_bone"),
            new TestModel.Node("NLG_scissor_cable", new[] { new TestModel.Box(3, -1, 1) }, Skeleton: 3)))!;

        Assert.Equal(18.0, outline.NoseM, 2);
    }

    // ── Rotors ───────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(12.0)]      // IVAO's H160
    [InlineData(45.0)]      // IVAO's EC175: read as a square, 7.1 m a side
    public void A_blurred_rotor_is_the_disc_inside_its_card_however_the_card_is_turned(double turnedDeg)
    {
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "blur" + turnedDeg.ToString(CultureInfo.InvariantCulture),
            Body("fuselage", new TestModel.Box(1, -6, 4)),
            new TestModel.Node("ENGINE_Propeller_Blurred_1_Blades", new[] { new TestModel.Box(5, -5, 5, Material: TestModel.Material.Blur) }, TurnedDeg: turnedDeg)))!;

        Assert.Equal(5.0, outline.HalfSpanM, 1);
        Assert.Equal(5.0, outline.NoseM, 1);
        // A disc, not a square: 4.5 m ahead of the hub it is 2.2 m wide a side.
        int nearTheRim = (int)Math.Round((outline.TailM + 4.5) / AircraftOutline.StationM);
        Assert.InRange(outline.HalfWidths[nearTheRim], 1.5, 3.0);
    }

    [Fact]
    public void A_see_through_picture_that_is_not_square_keeps_its_corners()
    {
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "pane", Body("fuselage", new TestModel.Box(1, -6, 4)),
            Body("canopy", new TestModel.Box(5, -2, 2, Material: TestModel.Material.Blur))))!;
        int atItsEdge = (int)Math.Round((outline.TailM + 1.8) / AircraftOutline.StationM);
        Assert.Equal(5.0, outline.HalfWidths[atItsEdge], 2);
    }

    [Fact]
    public void A_rotor_standing_still_takes_the_wider_side()
    {
        // Simaddons' Sea King, parked: five blades wherever they stopped, 10 and 13 m out, never at the same
        // station. The narrower side cut every one of them off and measured a 6.4 m helicopter.
        var notes = new List<string>();
        GroundTrafficModelReader.Notes = notes.Add;
        AircraftOutline outline;
        try
        {
            outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "stopped", Body("fuselage", new TestModel.Box(1, -6, 4)),
                Body("blade_1", new TestModel.Box(3, 1, 1.5, XShift: 4)), Body("blade_2", new TestModel.Box(3, -3, -2.5, XShift: -4))))!;
        }
        finally { GroundTrafficModelReader.Notes = null; }

        Assert.Equal(7.0, outline.HalfSpanM, 2);
        Assert.Contains(notes, n => n.Contains("rotor standing still"));
    }

    [Fact]
    public void Equipment_parked_on_one_side_is_still_dropped_and_said()
    {
        // A catering truck beside the nose: the wings reach as far on both sides, so this is no rotor.
        var notes = new List<string>();
        GroundTrafficModelReader.Notes = notes.Add;
        AircraftOutline outline;
        try
        {
            outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "truck", Body("fuselage", Fuselage), Body("wing", Wing),
                Body("x0_catertruck017", new TestModel.Box(4, 8, 12, XShift: 10))))!;
        }
        finally { GroundTrafficModelReader.Notes = null; }

        Assert.Equal(17.0, outline.HalfSpanM, 2);
        int besideTheNose = (int)Math.Round((outline.TailM + 10.0) / AircraftOutline.StationM);
        Assert.Equal(2.0, outline.HalfWidths[besideTheNose], 2);
        Assert.Contains(notes, n => n.Contains("one side is wider"));
    }

    // ── A scene that names the node it hangs on ──────────────────────────────────────────────────────

    // The wings, with the two nodes a wingtip device hangs on: 16 m out each side, 6 m behind the origin.
    private string WingsWithTipNodes(string name, string leftName = "Skel_Wing_Left_Wingtip", string? leftId = null)
        => TestModel.Write(_dir, name, Body("wing", new TestModel.Box(16, -8, 0)),
            new TestModel.Node(leftName, Translation: new[] { 16.0, 0.0, -6.0 }, UniqueId: leftId),
            new TestModel.Node("Skel_Wing_Right_Wingtip", Translation: new[] { -16.0, 0.0, -6.0 }));

    // A tip device 2 m wide: drawn about its own origin, outboard of it.
    private static TestModel.Node Tip(string name, int scene, bool left)
        => new(name, new[] { new TestModel.Box(1, -1.5, 1.5, XShift: left ? 1 : -1) }, Scene: scene);

    private AircraftOutline? WithTips(string wings, string tips)
        => GroundTrafficModelReader.Measure(new GroundTrafficModelReader.ModelPart[]
        {
            new(new[] { TestModel.Write(_dir, "tipfuselage", Body("fuselage", Fuselage)) }),
            new(new[] { wings }, Parent: 0), new(new[] { tips }, Parent: 0),
        });

    [Fact]
    public void A_scene_that_names_a_node_of_a_part_read_after_it_is_placed_all_the_same()
    {
        // The PMDG 737's fuselage hangs a scene on a node of the ROOT.gltf that follows it: the model is
        // assembled a second time, with every node the first time placed to look the name up in.
        string tips = TestModel.Write(_dir, "early-tips", null, new[] { null, "Skel_Wing_Left_Wingtip", "Skel_Wing_Right_Wingtip" },
            new TestModel.Node("Skel_SL_Box"), Tip("SL_L", 1, left: true), Tip("SL_R", 2, left: false));
        var outline = GroundTrafficModelReader.Measure(new GroundTrafficModelReader.ModelPart[]
        {
            new(new[] { TestModel.Write(_dir, "early-fuselage", Body("fuselage", Fuselage)) }),
            new(new[] { tips }, Parent: 0), new(new[] { WingsWithTipNodes("late-wings") }, Parent: 0),
        })!;
        Assert.Equal(18.0, outline.HalfSpanM, 2);
    }

    [Fact]
    public void A_chain_of_names_read_later_settles_instead_of_keeping_a_first_wrong_place()
    {
        // The tips hang on the wings' tip nodes, the wings on a node of a ROOT model, and each is read
        // BEFORE the part that holds its node. The first assembly leaves the wings at the origin; a second
        // one alone would hang the tips on the tip nodes as they stood then, 10 m ahead of the wings.
        string tips = TestModel.Write(_dir, "chain-tips", null, new[] { null, "Skel_Wing_Left_Wingtip", "Skel_Wing_Right_Wingtip" },
            new TestModel.Node("Skel_SL_Box"), Tip("SL_L", 1, left: true), Tip("SL_R", 2, left: false));
        string root = TestModel.Write(_dir, "chain-root", new TestModel.Node("WING_ROOT", Translation: new[] { 0.0, 0.0, -10.0 }));
        var outline = GroundTrafficModelReader.Measure(new GroundTrafficModelReader.ModelPart[]
        {
            new(new[] { TestModel.Write(_dir, "chain-fuselage", Body("fuselage", Fuselage)) }),
            new(new[] { tips }, Parent: 0),
            new(new[] { WingsWithTipNodes("chain-wings") }, Parent: 0, AttachNode: "WING_ROOT"),
            new(new[] { root }, Parent: 0),
        })!;

        Assert.Equal(18.0, outline.HalfSpanM, 2);
        // The tips are where the wings' tip nodes ended up: 16 m behind the origin, not 6.
        int widest = Enumerable.Range(0, outline.HalfWidths.Count).First(i => outline.HalfWidths[i] > 17.9f);
        Assert.Equal(-17.5, -outline.TailM + widest * AircraftOutline.StationM, 0);
    }

    [Fact]
    public void A_scene_tagged_with_a_node_of_another_part_hangs_on_that_node()
    {
        // The Fenix's sharklets: an untagged default scene, then one scene per wingtip.
        string tips = TestModel.Write(_dir, "sharklets", null, new[] { null, "Skel_Wing_Left_Wingtip", "Skel_Wing_Right_Wingtip" },
            new TestModel.Node("Skel_SL_Box"), Tip("SL_L", 1, left: true), Tip("SL_R", 2, left: false));
        var outline = WithTips(WingsWithTipNodes("wings-a"), tips)!;
        Assert.Equal(18.0, outline.HalfSpanM, 2);
        // The tips sit where the wing's tip nodes are, 6 m behind the origin: not at the origin.
        int widest = Enumerable.Range(0, outline.HalfWidths.Count).First(i => outline.HalfWidths[i] > 17.9f);
        Assert.Equal(-7.5, -outline.TailM + widest * AircraftOutline.StationM, 0);
    }

    [Fact]
    public void A_default_scene_that_is_tagged_hangs_on_its_node_too()
    {
        // The Fenix's wing fences: both scenes are tagged, the default one included.
        string tips = TestModel.Write(_dir, "fences", null, new[] { "Skel_Wing_Left_Wingtip", "Skel_Wing_Right_Wingtip" },
            Tip("WF_L", 0, left: true), Tip("WF_R", 1, left: false));
        Assert.Equal(18.0, WithTips(WingsWithTipNodes("wings-b"), tips)!.HalfSpanM, 2);
    }

    [Fact]
    public void The_node_is_found_by_its_unique_id_when_that_is_not_its_name()
    {
        string tips = TestModel.Write(_dir, "fences-id", null, new[] { "x0_Tip_Left", "Skel_Wing_Right_Wingtip" },
            Tip("WF_L", 0, left: true), Tip("WF_R", 1, left: false));
        Assert.Equal(18.0, WithTips(WingsWithTipNodes("wings-c", leftName: "Tip_Left", leftId: "x0_Tip_Left"), tips)!.HalfSpanM, 2);
    }

    [Fact]
    public void A_scene_whose_node_is_nowhere_is_left_out_and_an_untagged_extra_scene_always_is()
    {
        string wings = WingsWithTipNodes("wings-d");
        string lost = TestModel.Write(_dir, "lost", null, new[] { null, "No_Such_Node", "Skel_Wing_Right_Wingtip" },
            new TestModel.Node("Skel_SL_Box"), Tip("SL_L", 1, left: true), Tip("SL_R", 2, left: false));
        // One tip only: the outline is the narrower of its two sides, so the wing's own 16 m.
        Assert.Equal(16.0, WithTips(wings, lost)!.HalfSpanM, 2);
        string untagged = TestModel.Write(_dir, "untagged", null, new string?[] { null, null, null },
            new TestModel.Node("Skel_SL_Box"), Tip("SL_L", 1, left: true), Tip("SL_R", 2, left: false));
        Assert.Equal(16.0, WithTips(wings, untagged)!.HalfSpanM, 2);
    }

    [Fact]
    public void A_model_on_its_own_whose_default_scene_is_tagged_is_measured_where_it_is()
    {
        // Nothing to hang on: an ordinary model that happens to carry an id.
        var outline = GroundTrafficModelReader.Measure(TestModel.Write(_dir, "alone", null, new[] { "Some_Id" },
            Body("fuselage", Fuselage), Body("wing", Wing)))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);
    }

    [Fact]
    public void Nothing_readable_says_why()
    {
        Assert.Null(GroundTrafficModelReader.Measure(new GroundTrafficModelReader.ModelPart[] { new(new[] { Path.Combine(_dir, "gone.gltf") }) }, out string why));
        Assert.Equal("no geometry could be read", why);
        Assert.Null(GroundTrafficModelReader.Measure(new GroundTrafficModelReader.ModelPart[]
            { new(new[] { TestModel.Write(_dir, "huge", Body("fuselage", new TestModel.Box(3, -70, 70)), Body("wing", new TestModel.Box(30, -5, 5))) }) }, out why));
        Assert.StartsWith("not aircraft-sized", why);
    }

    // ── An FSX-era .MDL ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_legacy_model_is_measured_from_its_most_detailed_level()
    {
        // Its first level is a three-vertex stand-in on the fuselage: read alone the model has no wings.
        var outline = GroundTrafficModelReader.Measure(TestModel.WriteLegacy(_dir, "b734", Fuselage, Wing))!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        Assert.Equal(18.0, outline.NoseM, 2);
        Assert.Equal(22.0, outline.TailM, 2);
    }

    [Fact]
    public void A_file_that_only_calls_itself_a_legacy_model_measures_as_nothing()
    {
        string path = Path.Combine(_dir, "junk.mdl");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("RIFF\0\0\0\0MDLXVERT\xff\xff\xff\x7f junk junk junk"));
        Assert.Null(GroundTrafficModelReader.Measure(path));
        File.WriteAllBytes(path, new byte[8]);
        Assert.Null(GroundTrafficModelReader.Measure(path));
    }

    [Fact]
    public void A_legacy_model_of_nested_chunks_without_end_is_given_up_not_followed_down()
    {
        // Fifty thousand MDLD chunks, each holding the next: followed all the way down they overflowed the
        // stack, which no handler catches, and the app was gone.
        const int depth = 50_000;
        var file = new List<byte>(Encoding.ASCII.GetBytes("RIFF"));
        int total = 12 + depth * 8;
        file.AddRange(BitConverter.GetBytes(total - 8));
        file.AddRange(Encoding.ASCII.GetBytes("MDLX"));
        for (int k = 0; k < depth; k++)
        {
            file.AddRange(Encoding.ASCII.GetBytes("MDLD"));
            file.AddRange(BitConverter.GetBytes(total - (12 + k * 8) - 8));
        }
        string path = Path.Combine(_dir, "nested.mdl");
        File.WriteAllBytes(path, file.ToArray());
        Assert.Null(GroundTrafficModelReader.Measure(path));
    }

    // ── What a file is allowed to make the reader do ─────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]     // a path of ".." steps
    [InlineData(true)]      // an absolute path
    public void A_buffer_is_read_only_from_the_model_s_own_folder(bool absolute)
    {
        // The uri is text from the file: it named any file on the disk, to be read whole into memory.
        string elsewhere = Path.Combine(_dir, "elsewhere"), home = Path.Combine(_dir, "home");
        string real = TestModel.Write(elsewhere, "m", Body("fuselage", Fuselage), Body("wing", Wing));
        Assert.NotNull(GroundTrafficModelReader.Measure(real));

        Directory.CreateDirectory(home);
        string uri = absolute ? Path.Combine(elsewhere, "m.bin").Replace("\\", "/") : "../elsewhere/m.bin";
        string borrowed = Path.Combine(home, "m.gltf");
        File.WriteAllText(borrowed, File.ReadAllText(real).Replace("\"uri\":\"m.bin\"", "\"uri\":\"" + uri + "\""));
        Assert.Null(GroundTrafficModelReader.Measure(borrowed));
    }

    [Fact]
    public void Vertices_a_part_does_not_draw_do_not_shape_or_spoil_the_outline()
    {
        // The primitives of one mesh share their vertices: the slice of one that was left out (an
        // invisible helper 600 m away, a vertex that is not a number) is still in the array.
        double[] xz =
        {
            -17, -4, 17, -4, 17, 2, -17, 2,          // the wing
            -2, -22, 2, -22, 2, 18, -2, 18,          // the fuselage
            0, 600, double.NaN, 0,                   // never drawn
        };
        int[] drawn = { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        var outline = GroundTrafficModelReader.Silhouette(new[] { new GroundTrafficModelReader.Part(xz, drawn) })!;
        Assert.Equal(40.0, outline.LengthM, 1);
        Assert.Equal(17.0, outline.HalfSpanM, 2);
        // An index that is no vertex at all is left out, never read as vertex 0.
        int[] wild = { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, int.MinValue, 1, 6 };
        Assert.Equal(17.0, GroundTrafficModelReader.Silhouette(new[] { new GroundTrafficModelReader.Part(xz, wild) })!.HalfSpanM, 2);
    }

    // ── Installed packages ───────────────────────────────────────────────────────────────────────────

    private string Package(string package) => Path.Combine(_dir, "Community", package);

    private string Airplanes(string package, params string[] below)
        => Path.Combine(new[] { Package(package), "SimObjects", "Airplanes" }.Concat(below).ToArray());

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void An_old_model_cfg_naming_its_model_with_no_extension_leads_to_the_mdl()
    {
        string dir = Airplanes("ivao", "IVAO_B734");
        WriteText(Path.Combine(dir, "aircraft.cfg"), "[fltsim.0]\ntitle=IVAO B734 House\nmodel=\n");
        WriteText(Path.Combine(dir, "model", "model.cfg"), "[models]\nnormal=B734\n");
        TestModel.WriteLegacy(Path.Combine(dir, "model"), "B734", Fuselage, Wing);

        var index = GroundTrafficModelCatalog.BuildIndex(_dir);
        var parts = GroundTrafficModelCatalog.Parts(index["IVAO B734 House"]);
        Assert.EndsWith("B734.mdl", Assert.Single(Assert.Single(parts).Lods));
        Assert.Equal(17.0, GroundTrafficModelReader.Measure(parts)!.HalfSpanM, 2);
    }

    [Fact]
    public void A_model_that_takes_its_levels_from_a_template_and_merges_another_into_them_is_both()
    {
        // The Fenix's sharklet wings: the plain wings' levels of detail, plus the sharklets merged in.
        string dir = Airplanes("fnx", "A320", "attachments", "fnx", "Part_Wings", "model");
        TestModel.Write(dir, "Wings_LOD0", Body("wing", Wing));
        TestModel.Write(dir, "Wings_LOD1", Body("wing", Wing));
        TestModel.Write(dir, "Sharklets_LOD0", Body("sharklets", new TestModel.Box(18.5, -4, -2)));
        WriteText(Path.Combine(dir, "Wings.xml"), "<ModelInfo><LODS><LOD minSize=\"50\" ModelFile=\"Wings_LOD0.gltf\"/><LOD ModelFile=\"Wings_LOD1.gltf\"/></LODS></ModelInfo>");
        WriteText(Path.Combine(dir, "WingsSharklets.xml"), "<ModelInfo>\n <LODS Template=\"Wings.xml\"/>\n <MergeModel> Sharklets_LOD0.gltf </MergeModel>\n</ModelInfo>");

        var groups = GroundTrafficModelCatalog.ModelGroups(Path.Combine(dir, "WingsSharklets.xml"), dir, 0);
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { "Wings_LOD0.gltf", "Wings_LOD1.gltf" }, groups[0].Select(Path.GetFileName));
        Assert.Equal("Sharklets_LOD0.gltf", Path.GetFileName(Assert.Single(groups[1])));

        // A template that names itself, or one that is not there, ends: no model, no loop.
        WriteText(Path.Combine(dir, "Loop.xml"), "<ModelInfo><LODS Template=\"Loop.xml\"/></ModelInfo>");
        string elsewhere = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(elsewhere);
        WriteText(Path.Combine(elsewhere, "Orphan.xml"), "<ModelInfo><LODS Template=\"Gone.xml\"/></ModelInfo>");
        Assert.All(GroundTrafficModelCatalog.ModelGroups(Path.Combine(elsewhere, "Orphan.xml"), elsewhere, 0), g => Assert.Empty(g));
        GroundTrafficModelCatalog.ModelGroups(Path.Combine(dir, "Loop.xml"), dir, 0);
    }

    // An MSFS 2024 modular aircraft: presets/<maker>/<preset>/config holds its aircraft.cfg and the list of
    // what its exterior is assembled from; one attachment lives in ANOTHER package (the Fenix's fuselage is
    // in its common package), and the simulator's file system overlays the two.
    private void InstallModularJet(string title = "Jet 900 House")
    {
        string config = Airplanes("jet", "Jet", "presets", "maker", "v900", "config");
        WriteText(Path.Combine(config, "aircraft.cfg"), $"[VERSION]\nmajor=1\n[FLTSIM.0]\ntitle = \"{title}\"\nmodel = \"\"\n");
        const string root = "SimObjects/Airplanes/Jet/attachments/maker/";
        WriteText(Path.Combine(config, "attached_objects.cfg"),
            "[SIM_ATTACHMENT.0]\nattachment_root = \"" + root + "Part_Fuselage\"\nattachment_file = \"model/Fuselage.xml\"\nattach_to_model = \"exterior\"\nattach_to_node = \"Attach_Fuselage\"\n"
            + "[SIM_ATTACHMENT.1]\nattachment_root = \"" + root + "Part_Emergency_Slides\"\nattachment_file = \"model/Slides.gltf\"\nattach_to_model = \"exterior\"\n"
            + "[SIM_ATTACHMENT.2]\nattachment_root = \"" + root + "Part_Seats\"\nattachment_file = \"model/Seats.gltf\"\nattach_to_model = \"interior\"\n"
            + "[SIM_ATTACHMENT.3]\nattachment_root = \"" + root + "Function_Exterior\"\nattachment_file = \"model/Nothing.xml\"\nattach_to_model = \"exterior\"\n"
            + "[MERGE_MODEL.0]\nmerge_file = \"" + root + "Part_Tail/model/Tail.gltf\"\nmerge_to_model = \"exterior\"\n");

        string common = Airplanes("jet", "Jet", "common", "model");
        WriteText(Path.Combine(common, "model.cfg"), "[models]\nexterior=Common.gltf\n");
        TestModel.Write(common, "Common", new TestModel.Node("Attach_Fuselage", Translation: new[] { 0.0, 0.0, 5.0 }));

        string fuselage = Airplanes("jet", "Jet", "attachments", "maker", "Part_Fuselage");
        WriteText(Path.Combine(fuselage, "model", "Fuselage.xml"), "<ModelInfo><LODS><LOD ModelFile=\"Fuselage_LOD0.gltf\"/></LODS></ModelInfo>");
        TestModel.Write(Path.Combine(fuselage, "model"), "Fuselage_LOD0", Body("fuselage", Fuselage),
            new TestModel.Node("Attach_Wings", Translation: new[] { 0.0, 0.0, -10.0 }));
        // The fuselage carries the wings, and the wings are in the other package.
        WriteText(Path.Combine(fuselage, "config", "attached_objects.cfg"),
            "[SIM_ATTACHMENT.0]\nattachment_root = \"" + root + "Part_Wings\"\nattachment_file = \"model/Wings.gltf\"\nattach_to_model = \"exterior\"\nattach_to_node = \"Attach_Wings\"\n");
        TestModel.Write(Path.Combine(Airplanes("jet-common", "Jet", "attachments", "maker", "Part_Wings"), "model"), "Wings", Body("wing", Wing));

        TestModel.Write(Path.Combine(Airplanes("jet", "Jet", "attachments", "maker", "Part_Emergency_Slides"), "model"), "Slides",
            Body("slide", new TestModel.Box(30, 0, 4)));
        TestModel.Write(Path.Combine(Airplanes("jet", "Jet", "attachments", "maker", "Part_Seats"), "model"), "Seats",
            Body("seats", new TestModel.Box(30, 0, 4)));
        TestModel.Write(Path.Combine(Airplanes("jet", "Jet", "attachments", "maker", "Part_Tail"), "model"), "Tail",
            Body("tail", new TestModel.Box(1, -30, -16)));
    }

    [Fact]
    public void A_modular_aircraft_is_assembled_from_its_exterior_attachments_across_packages()
    {
        InstallModularJet();
        var index = GroundTrafficModelCatalog.BuildIndex(_dir);
        var parts = GroundTrafficModelCatalog.Parts(index["Jet 900 House"]);

        // The common model, the fuselage on its node, the wings on the fuselage's node, the merged tail:
        // never the evacuation slides, never what goes to the interior.
        Assert.Equal(new[] { "Common.gltf", "Fuselage_LOD0.gltf", "Wings.gltf", "Tail.gltf" }, parts.Select(p => Path.GetFileName(p.Lods[0])));
        Assert.Equal(new[] { -1, 0, 1, 0 }, parts.Select(p => p.Parent));
        Assert.Equal(new[] { null, "Attach_Fuselage", "Attach_Wings", null }, parts.Select(p => p.AttachNode));
        Assert.Contains(Path.Combine("jet-common", "SimObjects"), parts[2].Lods[0]);

        var outline = GroundTrafficModelReader.Measure(parts)!;
        Assert.Equal(17.0, outline.HalfSpanM, 2);     // not the slides' 30
        Assert.Equal(23.0, outline.NoseM, 2);         // the fuselage, 5 m ahead on its node
        Assert.Equal(30.0, outline.TailM, 2);         // the merged tail, at the common model's origin
        int widest = Enumerable.Range(0, outline.HalfWidths.Count).First(i => outline.HalfWidths[i] > 16.9f);
        Assert.Equal(-9.0, -outline.TailM + widest * AircraftOutline.StationM, 0);
    }

    [Fact]
    public void A_preset_takes_the_type_its_aircraft_declares_in_its_common_config_or_its_attachments()
    {
        InstallModularJet();
        Assert.Equal("", GroundTrafficModelCatalog.BuildIndex(_dir)["Jet 900 House"].Icao);
        string Config(string package, string part) => Path.Combine(Airplanes(package, "Jet", "attachments", "maker", part, "config"), "aircraft.cfg");

        // No part the preset's attachment lists name declares one: the one every part of the aircraft agrees on
        // (the tail is merged in, no list names it).
        WriteText(Config("jet", "Part_Tail"), "[GENERAL]\nicao_type_designator = B737\n");
        Assert.Equal("B737", GroundTrafficModelCatalog.BuildIndex(_dir)["Jet 900 House"].Icao);

        // Declared by a part the preset is assembled from (PMDG: each fuselage part), wherever that part lives.
        // The Fenix: the exterior part the preset attaches declares none and INHERITS the fuselage, which is in
        // the package its A319, A320 and A321 share, each fuselage declaring its own; no list names a fuselage.
        WriteText(Path.Combine(Airplanes("jet", "Jet", "attachments", "maker", "Function_Exterior"), "attachment.cfg"),
            "[VERSION]\nmajor = 1\n[Inherit]\nbase = \"SimObjects\\Airplanes\\Jet\\attachments\\maker\\Part_Body_900\"\n");
        WriteText(Path.Combine(Airplanes("jet-common", "Jet", "attachments", "maker", "Part_Body_900"), "attachment.cfg"),
            "[Inherit]\nbase = \"SimObjects/Airplanes/Jet/attachments/maker/Part_Body\"\n[Tags]\ntag.0 = \"900\"\n");
        WriteText(Config("jet-common", "Part_Body_900"), "[GENERAL]\nicao_type_designator=\"B739\"\n");
        WriteText(Config("jet-common", "Part_Body_600"), "[GENERAL]\nicao_type_designator=\"B736\"\n");
        Assert.Equal("B739", GroundTrafficModelCatalog.BuildIndex(_dir)["Jet 900 House"].Icao);

        // Two of its own parts that disagree (the wings hang on the fuselage, in the other package): none.
        WriteText(Config("jet-common", "Part_Wings"), "[GENERAL]\nicao_type_designator = B738\n");
        Assert.Equal("", GroundTrafficModelCatalog.BuildIndex(_dir)["Jet 900 House"].Icao);

        // Declared once for the aircraft, in its common config (the TFDi MD-11, the FlyByWire A380): that one.
        WriteText(Path.Combine(Airplanes("jet", "Jet", "common", "config"), "aircraft.cfg"),
            "[GENERAL]\n;icao_type_designator = A359\nicao_type_designator = \"b738\" ; the type\n");
        Assert.Equal("B738", GroundTrafficModelCatalog.BuildIndex(_dir)["Jet 900 House"].Icao);
    }

    [Fact]
    public void A_preset_that_names_its_model_is_measured_from_the_common_model_of_that_name()
    {
        // The TFDi MD-11: presets/<maker>/<preset>/config/aircraft.cfg says model = "GE", the whole airframe is
        // common/model.GE, and the one thing attached carries no geometry at all (its navigation graph).
        string config = Airplanes("md11", "MD-11", "presets", "maker", "MD-11 GE", "config");
        WriteText(Path.Combine(config, "aircraft.cfg"), "[FLTSIM.0]\ntitle = \"MD-11 GE\"\nmodel = \"GE\"\n");
        WriteText(Path.Combine(config, "attached_objects.cfg"),
            "[SIM_ATTACHMENT.0]\nattachment_root = \"SimObjects/Airplanes/MD-11/attachments/maker/PAX_GE_Graph\"\nalias = \"PAX_GE_Graph\"\n");
        WriteText(Path.Combine(Airplanes("md11", "MD-11", "attachments", "maker", "PAX_GE_Graph"), "attachment.cfg"), "[Tags]\ntag.0 = \"pax\"\n");
        string model = Airplanes("md11", "MD-11", "common", "model.GE");
        WriteText(Path.Combine(model, "model.cfg"), "[models]\nexterior=MD11_EXT.xml\n");
        WriteText(Path.Combine(model, "MD11_EXT.xml"), "<ModelInfo><LODS><LOD ModelFile=\"MD11_LOD0.gltf\"/></LODS></ModelInfo>");
        TestModel.Write(model, "MD11_LOD0", Body("fuselage", new TestModel.Box(3, -33, 28)), Body("wing", new TestModel.Box(26, -8, 2)));

        var index = GroundTrafficModelCatalog.BuildIndex(_dir);
        var outline = GroundTrafficModelReader.Measure(GroundTrafficModelCatalog.Parts(index["MD-11 GE"]), out _)!;
        Assert.Equal(26.0, outline.HalfSpanM, 2);
        Assert.Equal(61.0, outline.LengthM, 2);
    }

    [Fact]
    public void A_modular_aircraft_with_nothing_attached_on_disk_has_no_model()
    {
        string config = Airplanes("jet", "Bare", "presets", "maker", "v1", "config");
        WriteText(Path.Combine(config, "aircraft.cfg"), "[FLTSIM.0]\ntitle = \"Bare\"\n");
        WriteText(Path.Combine(config, "attached_objects.cfg"),
            "[SIM_ATTACHMENT.0]\nattachment_root = \"SimObjects/Airplanes/Bare/attachments/maker/Streamed\"\nattachment_file = \"model/Gone.gltf\"\nattach_to_model = \"exterior\"\n");
        string common = Airplanes("jet", "Bare", "common", "model");
        TestModel.Write(common, "Common", new TestModel.Node("root"));
        var index = GroundTrafficModelCatalog.BuildIndex(_dir);
        Assert.Empty(GroundTrafficModelCatalog.Parts(index["Bare"]));
    }

    [Fact]
    public void A_livery_package_naming_the_base_aircraft_s_model_folder_finds_it_in_the_base_package()
    {
        // The COWS DA40's liveries: model = "\..\..\COWS_DA40NG\model.BOOM", a folder only the base package has.
        string baseModel = Airplanes("cows-da40", "COWS_DA40NG", "model.BOOM");
        WriteText(Path.Combine(baseModel, "model.cfg"), "[models]\nnormal=DA40.xml\n");
        WriteText(Path.Combine(baseModel, "DA40.xml"), "<ModelInfo><LODS><LOD ModelFile=\"DA40_LOD0.gltf\"/></LODS></ModelInfo>");
        TestModel.Write(baseModel, "DA40_LOD0", Body("fuselage", new TestModel.Box(0.6, -4, 4)), Body("wing", new TestModel.Box(6, -1, 0.5)));
        string livery = Airplanes("da40-livery", "COWS_DA40NG_Blue");
        WriteText(Path.Combine(livery, "aircraft.cfg"), "[FLTSIM.0]\ntitle = \"DA40 NG Blue\"\nmodel = \"\\..\\..\\COWS_DA40NG\\model.BOOM\"\n");

        var index = GroundTrafficModelCatalog.BuildIndex(_dir);
        var outline = GroundTrafficModelReader.Measure(GroundTrafficModelCatalog.Parts(index["DA40 NG Blue"]))!;
        Assert.Equal(6.0, outline.HalfSpanM, 2);
        Assert.Equal(8.0, outline.LengthM, 2);
    }

    [Fact]
    public void The_packages_stamp_changes_with_a_package_and_with_a_livery_folder_and_not_otherwise()
    {
        InstallModularJet();
        string before = GroundTrafficModelCatalog.PackagesStamp(_dir);
        Assert.Equal(before, GroundTrafficModelCatalog.PackagesStamp(_dir));

        Directory.CreateDirectory(Package("new-package"));
        string withPackage = GroundTrafficModelCatalog.PackagesStamp(_dir);
        Assert.NotEqual(before, withPackage);

        // A livery added by a traffic library's manager: a new folder under SimObjects\Airplanes.
        string airplanes = Airplanes("jet");
        Directory.SetLastWriteTimeUtc(airplanes, Directory.GetLastWriteTimeUtc(airplanes).AddMinutes(1));
        Assert.NotEqual(withPackage, GroundTrafficModelCatalog.PackagesStamp(_dir));
    }

    [Fact]
    public void A_package_installed_while_the_app_runs_is_found_once_the_unknown_title_is_due_again()
    {
        InstallModularJet();
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        using var catalog = new GroundTrafficModelCatalog(null, () => _dir, () => now);
        const string title = "AIGAIM_New Airline Airbus A320";

        Assert.Null(catalog.TryGet(title));
        Assert.True(SpinWait.SpinUntil(() => catalog.IsSettled(title), 10_000));
        Assert.Null(catalog.TryGet(title));

        string dir = Airplanes("new-traffic", "AIGAIM_New_A320");
        WriteText(Path.Combine(dir, "aircraft.cfg"), "[FLTSIM.0]\ntitle=" + title + "\nmodel=\n");
        WriteText(Path.Combine(dir, "model", "model.cfg"), "[models]\nnormal=m.gltf\n");
        TestModel.Write(Path.Combine(dir, "model"), "m", Body("fuselage", Fuselage), Body("wing", Wing));

        // Inside the five minutes the title is not looked for again: no package scan per unknown aircraft.
        now = now.AddMinutes(GroundTrafficModelCatalog.ReindexMinutes - 1);
        Assert.Null(catalog.TryGet(title));
        Assert.True(catalog.IsSettled(title));

        now = now.AddMinutes(2);
        Assert.Null(catalog.TryGet(title));             // queued again
        Assert.True(SpinWait.SpinUntil(() => catalog.TryGet(title) != null, 10_000));
        Assert.Equal(17.0, catalog.TryGet(title)!.HalfSpanM, 2);
    }

    [Fact]
    public void A_model_that_could_not_be_read_is_tried_again_next_time_not_remembered_as_unmeasurable()
    {
        // Its geometry is in use by another program the first time it is seen (a virus scan, a sync
        // client). Kept, that one passing failure sized the model from the table in every later session.
        string dir = Airplanes("locked", "AIG_Locked");
        WriteText(Path.Combine(dir, "aircraft.cfg"), "[FLTSIM.0]\ntitle=Locked\nmodel=\n");
        WriteText(Path.Combine(dir, "model", "model.cfg"), "[models]\nnormal=m.gltf\n");
        TestModel.Write(Path.Combine(dir, "model"), "m", Body("fuselage", Fuselage), Body("wing", Wing));
        string cache = Path.Combine(_dir, "cache-locked");

        using (new FileStream(Path.Combine(dir, "model", "m.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
        using (var first = new GroundTrafficModelCatalog(cache, () => _dir))
        {
            first.TryGet("Locked");
            Assert.True(SpinWait.SpinUntil(() => first.IsSettled("Locked"), 10_000));
            Assert.Null(first.TryGet("Locked"));
        }

        using var second = new GroundTrafficModelCatalog(cache, () => _dir);
        second.TryGet("Locked");
        Assert.True(SpinWait.SpinUntil(() => second.TryGet("Locked") != null, 10_000));
        Assert.Equal(17.0, second.TryGet("Locked")!.HalfSpanM, 2);
    }

    [Fact]
    public void A_package_updated_while_the_app_runs_has_its_model_measured_again()
    {
        string dir = Airplanes("updated", "AIG_Updated");
        WriteText(Path.Combine(dir, "aircraft.cfg"), "[FLTSIM.0]\ntitle=Updated\nmodel=\n");
        WriteText(Path.Combine(dir, "model", "model.cfg"), "[models]\nnormal=m.gltf\n");
        TestModel.Write(Path.Combine(dir, "model"), "m", Body("fuselage", Fuselage), Body("wing", Wing));
        var now = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        using var catalog = new GroundTrafficModelCatalog(null, () => _dir, () => now);

        catalog.TryGet("Updated");
        Assert.True(SpinWait.SpinUntil(() => catalog.TryGet("Updated") != null, 10_000));
        Assert.Equal(17.0, catalog.TryGet("Updated")!.HalfSpanM, 2);

        // The update: a wider model under the same title, and a livery folder beside it (what moves the
        // packages' stamp). The index is read again when a title nobody knows is next asked for.
        TestModel.Write(Path.Combine(dir, "model"), "m", Body("fuselage", Fuselage), Body("wing", new TestModel.Box(20, -4, 2)), Body("fin", new TestModel.Box(0.5, -22, -18)));
        string added = Airplanes("updated", "AIG_Updated_New");
        WriteText(Path.Combine(added, "aircraft.cfg"), "[FLTSIM.0]\ntitle=Newcomer\nmodel=\n");
        now = now.AddMinutes(GroundTrafficModelCatalog.ReindexMinutes + 1);
        catalog.TryGet("Newcomer");
        Assert.True(SpinWait.SpinUntil(() => catalog.IsSettled("Newcomer"), 10_000));

        Assert.True(SpinWait.SpinUntil(() => catalog.TryGet("Updated") is { } outline && Math.Abs(outline.HalfSpanM - 20.0) < 0.01, 10_000));
    }

    [Fact]
    public void A_changed_model_file_is_measured_again_and_an_unchanged_one_is_not()
    {
        string dir = Airplanes("traffic", "AIG_A320");
        WriteText(Path.Combine(dir, "aircraft.cfg"), "[FLTSIM.0]\ntitle=First\nmodel=\n[FLTSIM.1]\ntitle=Second\nmodel=\n");
        WriteText(Path.Combine(dir, "model", "model.cfg"), "[models]\nnormal=m.gltf\n");
        TestModel.Write(Path.Combine(dir, "model"), "m", Body("fuselage", Fuselage), Body("wing", Wing));
        string cache = Path.Combine(_dir, "cache");

        using (var catalog = new GroundTrafficModelCatalog(cache, () => _dir))
        {
            catalog.TryGet("First");
            catalog.TryGet("Second");
            Assert.True(SpinWait.SpinUntil(() => catalog.IsSettled("First") && catalog.IsSettled("Second"), 10_000));
            // Two liveries of one model share one measurement.
            Assert.Same(catalog.TryGet("First"), catalog.TryGet("Second"));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(cache, "outlines.json")), 10_000));
        }

        // The model is replaced by a wider one (an update): its size and date differ, so the kept outline is not used.
        TestModel.Write(Path.Combine(dir, "model"), "m", Body("fuselage", Fuselage), Body("wing", new TestModel.Box(20, -4, 2)), Body("fin", new TestModel.Box(0.5, -22, -18)));
        using var second = new GroundTrafficModelCatalog(cache, () => _dir);
        second.TryGet("First");
        Assert.True(SpinWait.SpinUntil(() => second.IsSettled("First"), 10_000));
        Assert.Equal(20.0, second.TryGet("First")!.HalfSpanM, 2);
    }

    [Fact]
    public void A_damaged_cache_file_is_rebuilt_not_fatal()
    {
        string dir = Airplanes("traffic", "AIG_A320");
        WriteText(Path.Combine(dir, "aircraft.cfg"), "[FLTSIM.0]\ntitle=First\nmodel=\n");
        WriteText(Path.Combine(dir, "model", "model.cfg"), "[models]\nnormal=m.gltf\n");
        TestModel.Write(Path.Combine(dir, "model"), "m", Body("fuselage", Fuselage), Body("wing", Wing));
        string cache = Path.Combine(_dir, "cache");
        WriteText(Path.Combine(cache, "outlines.json"), "{ \"Version\": 1, \"Models\": [ { \"Key\": ");

        using var catalog = new GroundTrafficModelCatalog(cache, () => _dir);
        catalog.TryGet("First");
        Assert.True(SpinWait.SpinUntil(() => catalog.IsSettled("First"), 10_000));
        Assert.Equal(17.0, catalog.TryGet("First")!.HalfSpanM, 2);
    }

    // ── The own aircraft's outline ───────────────────────────────────────────────────────────────────

    private GroundTrafficModelCatalog OwnCatalog(string title, double wingHalfWidth)
    {
        string dir = Airplanes("own", "Own_" + Guid.NewGuid().ToString("N"));
        WriteText(Path.Combine(dir, "aircraft.cfg"), "[FLTSIM.0]\ntitle=" + title + "\nmodel=\n");
        WriteText(Path.Combine(dir, "model", "model.cfg"), "[models]\nnormal=m.gltf\n");
        TestModel.Write(Path.Combine(dir, "model"), "m", Body("fuselage", Fuselage), Body("wing", new TestModel.Box(wingHalfWidth, -4, 2)));
        return new GroundTrafficModelCatalog(null, () => _dir);
    }

    private static AircraftHull OwnHullOnceMeasured(GroundTrafficHarness h, GroundTrafficModelCatalog catalog, string title)
    {
        // Before its model is measured the own aircraft has its type's published size, never nothing.
        var first = h.Monitor.OwnHullForHarness();
        Assert.NotEqual(AircraftHullSource.Default, first.Source);
        Assert.True(SpinWait.SpinUntil(() => catalog.IsSettled(title), 10_000));
        return h.Monitor.OwnHullForHarness();
    }

    [Theory]
    [InlineData(16.3)]     // narrower than the flight model's figure
    [InlineData(18.5)]     // wider than it
    public void The_own_aircraft_s_outline_is_the_one_measured_not_the_span_its_flight_model_reports(double wingHalfWidth)
    {
        // A flight model's wing span is a number its author typed: the PMDG 777's says 62.7 m for a wing
        // that is, and measures, 64.7 m. Within the guard's tolerance the measurement stands as it is.
        string title = FormattableString.Invariant($"Own A320 {wingHalfWidth}");
        using var catalog = OwnCatalog(title, wingHalfWidth);
        var h = new GroundTrafficHarness();
        h.UseRealSizes(models: catalog);
        h.Monitor.OwnAircraftProvider = () => (title, "A320", 117.5);

        var hull = OwnHullOnceMeasured(h, catalog, title);
        Assert.Equal(AircraftHullSource.Model, hull.Source);
        Assert.Equal(wingHalfWidth, hull.HalfSpanM, 2);
        Assert.Equal(18.0, hull.NoseM, 2);              // the measured nose and tail, not 0.46 of a length
        Assert.Equal(22.0, hull.TailM, 2);
    }

    [Fact]
    public void The_own_outline_is_held_against_the_type_s_published_size_not_the_span_its_flight_model_reports()
    {
        // A flight model whose wing span is a third short (24.4 m for an A320): the measured outline is
        // the better evidence, and it fits the type. Held against that span it was thrown away, and the
        // aircraft sized from the wrong span instead.
        const string title = "Own A320 Odd Span";
        using var catalog = OwnCatalog(title, wingHalfWidth: 17.9);
        var h = new GroundTrafficHarness();
        h.UseRealSizes(models: catalog);
        h.Monitor.OwnAircraftProvider = () => (title, "A320", 80.0);

        var hull = OwnHullOnceMeasured(h, catalog, title);
        Assert.Equal(AircraftHullSource.Model, hull.Source);
        Assert.Equal(17.9, hull.HalfSpanM, 2);
    }

    [Fact]
    public void An_own_outline_far_from_the_aircraft_s_size_is_not_believed()
    {
        // A wing that measures 14 m on an aircraft whose flight model says 35.81 m was not assembled:
        // the type's published size is used, never a fragment.
        const string title = "Own A320 Broken";
        using var catalog = OwnCatalog(title, wingHalfWidth: 7);
        var h = new GroundTrafficHarness();
        h.UseRealSizes(models: catalog);
        h.Monitor.OwnAircraftProvider = () => (title, "A320", 117.5);

        var hull = OwnHullOnceMeasured(h, catalog, title);
        Assert.Equal(AircraftHullSource.Table, hull.Source);
        Assert.Equal(117.5 * 0.3048 / 2.0, hull.HalfSpanM, 2);
    }

    [Fact]
    public void An_own_aircraft_nobody_has_named_yet_is_the_reference_widebody()
    {
        var h = new GroundTrafficHarness();
        h.UseRealSizes();
        Assert.Equal(AircraftHullSource.Default, h.Monitor.OwnHullForHarness().Source);
        // A provider that throws (the simulator connection going away) changes nothing.
        h.Monitor.OwnAircraftProvider = () => throw new InvalidOperationException("gone");
        Assert.Equal(AircraftHullSource.Default, h.Monitor.OwnHullForHarness().Source);
    }

    [Fact]
    public void Changing_aircraft_resolves_the_own_outline_again()
    {
        var h = new GroundTrafficHarness();
        h.UseRealSizes();
        var own = (Title: "", Icao: "A320", SpanFt: 117.5);
        h.Monitor.OwnAircraftProvider = () => (own.Title, own.Icao, own.SpanFt);
        Assert.Equal("A320", h.Monitor.OwnHullForHarness().Icao);
        own = ("", "B77W", 212.6);
        var heavy = h.Monitor.OwnHullForHarness();
        Assert.Equal("B77W", heavy.Icao);
        Assert.Equal(212.6 * 0.3048 / 2.0, heavy.HalfSpanM, 1);
    }
}
