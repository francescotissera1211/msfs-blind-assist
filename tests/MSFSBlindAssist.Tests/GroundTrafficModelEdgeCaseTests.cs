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
    public enum Material { Visible, Invisible, Occluder, InvisibleSwitchedOff }

    /// <summary>A flat box: x from XShift - HalfWidth to XShift + HalfWidth, z from ZMin to ZMax.</summary>
    public sealed record Box(double HalfWidth, double ZMin, double ZMax, double XShift = 0, Material Material = Material.Visible);

    /// <summary>How a node's boxes share buffers: each its own, or one vertex and one index accessor the way MSFS packs materials.</summary>
    public enum Packing { Separate, StartIndex, BaseVertex }

    /// <param name="UniqueId">Its <c>ASOBO_unique_id</c>, when it has one.</param>
    /// <param name="Scene">The scene a root node belongs to (0 is the default scene).</param>
    public sealed record Node(string Name, Box[]? Boxes = null, double[]? Translation = null, int[]? Children = null,
        bool Skinned = false, string? AnimatedBy = null, Packing Packing = Packing.Separate, string? UniqueId = null, int Scene = 0);

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
            if (node.Skinned) json.Append(",\"skin\":0");
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
            + "],\"materials\":[{\"name\":\"Paint\"},{\"name\":\"Inv Col\",\"extensions\":{\"ASOBO_material_invisible\":{}}},{\"name\":\"Occluder\",\"extensions\":{\"ASOBO_material_environment_occluder\":{}}},{\"name\":\"Shown\",\"extensions\":{\"ASOBO_material_invisible\":{\"enabled\":false}}}]"
            + ",\"meshes\":[" + string.Join(",", meshes) + "],\"accessors\":[" + string.Join(",", accessors)
            + "],\"bufferViews\":[" + string.Join(",", views)
            + "],\"buffers\":[{\"uri\":\"" + name + ".bin\",\"byteLength\":" + (declaredBytes ?? bin.Count).ToString(CultureInfo.InvariantCulture) + "}]"
            + (animations.Count > 0 ? ",\"animations\":[" + string.Join(",", animations) + "]" : "")
            + (nodes.Any(n => n.Skinned) ? ",\"skins\":[{\"joints\":[0]}]" : "") + "}";
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
