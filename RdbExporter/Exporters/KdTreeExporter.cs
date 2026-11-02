using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Converts KD_n::Tree_c collision trees ("KDv2") to OBJ, one group per leaf in triangle order, with each run of
    /// triangle tags as a comment. 1000015 holds per-playfield water surfaces (id = playfield × 10000), 1000028
    /// per-object collision (same ids as 1010060); the client never loads either.
    /// Layout: "KDv2", u32 slot count, slots × 16-byte nodes, then 40-byte triangles to the end.
    /// Node: i16 min[3], i16 max[3] (floor / ceil of the triangles below), u32 w: bit 31 leaf, low 30 bits the byte offset
    /// to the next node to test when the box misses (0 = stop), so the nodes are a bounding-volume tree in preorder.
    /// A leaf takes two slots; the second is u32 first triangle, u32 count, u32 (KDv3's vertex base), u32 0.
    /// Triangle: 3 × f32[3] vertices, u32 tag.
    /// </summary>
    public class KdTreeExporter : IExporter
    {
        private const uint Leaf = 0x80000000;
        private const uint Skip = 0x3fffffff;

        private record LeafInfo(int Slot, int First, int Count);

        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    var obj = Convert(entry.ReadAllBytes(parameters.SwlInstallDir));
                    File.WriteAllText(Helpers.GetOutputPath(parameters, entry, ".obj", true), obj, new UTF8Encoding(false));
                }
                catch (InvalidDataException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }

        private static string Convert(byte[] data)
        {
            var r = new SpanReader(data);
            var magic = r.Latin1(4);
            if (magic != "KDv2") throw new InvalidDataException(magic == "KDv3" ? "KDv3 (indexed vertices) isn't supported." : "No KDv2 magic.");
            var slots = (int)r.U32();
            var rest = data.Length - 8L - 16L * slots;
            if (rest < 0 || rest % 40 != 0) throw new InvalidDataException($"{slots} slots leave {rest} bytes.");
            var triangleCount = (int)(rest / 40);
            r.Position = 8 + 16 * slots;
            var vertices = new float[triangleCount][];
            var tags = new uint[triangleCount];
            for (var i = 0; i < triangleCount; i++)
            {
                vertices[i] = r.F32s(9);
                tags[i] = r.U32();
            }

            var obj = new StringBuilder();
            if (slots == 0)
            {
                if (triangleCount > 0) throw new InvalidDataException("Triangles without nodes.");
                obj.AppendLine("# empty tree");
                return obj.ToString();
            }

            var leaves = new List<LeafInfo>();
            var end = Node(data, 0, slots, vertices, leaves, out _);
            if (end != slots) throw new InvalidDataException($"The tree ends at slot {end} of {slots}.");
            leaves.Sort((a, b) => a.First.CompareTo(b.First));
            var position = 0;
            foreach (var leaf in leaves)
            {
                if (leaf.First != position) throw new InvalidDataException($"Triangles {position} to {leaf.First} are in no leaf, or shared.");
                position += leaf.Count;
            }
            if (position != triangleCount) throw new InvalidDataException($"The leaves cover {position} of {triangleCount} triangles.");

            foreach (var v in vertices)
            {
                for (var k = 0; k < 9; k += 3) obj.AppendLine($"v {F(v[k])} {F(v[k + 1])} {F(v[k + 2])}");
            }
            foreach (var leaf in leaves)
            {
                obj.AppendLine($"g leaf{leaf.Slot}");
                uint? tag = null;
                for (var j = leaf.First; j < leaf.First + leaf.Count; j++)
                {
                    if (tags[j] != tag)
                    {
                        tag = tags[j];
                        obj.AppendLine($"# tag 0x{tag:x}");
                    }
                    obj.AppendLine($"f {3 * j + 1} {3 * j + 2} {3 * j + 3}");
                }
            }
            return obj.ToString();
        }

        /// <summary>Checks the subtree at slot k and returns the slot after it.</summary>
        private static int Node(byte[] data, int k, int slots, float[][] vertices, List<LeafInfo> leaves, out int[] box)
        {
            if (k >= slots) throw new InvalidDataException($"Slot {k} past {slots}.");
            var r = new SpanReader(data) { Position = 8 + 16 * k };
            box = Enumerable.Range(0, 6).Select(_ => (int)(short)r.U16()).ToArray();
            var w = r.U32();
            if ((w & 0x40000000) != 0) throw new InvalidDataException($"Slot {k} has bit 30 set.");
            int end;
            int[] want;
            if ((w & Leaf) != 0)
            {
                if (k + 1 >= slots) throw new InvalidDataException($"Leaf {k} has no second slot.");
                var first = r.U32();
                var count = r.U32();
                r.U32();
                if (r.U32() != 0) throw new InvalidDataException($"Leaf {k}'s last word isn't 0.");
                if (count == 0 || first + count > vertices.Length) throw new InvalidDataException($"Leaf {k} has triangles {first} + {count} of {vertices.Length}.");
                want = new[] { int.MaxValue, int.MaxValue, int.MaxValue, int.MinValue, int.MinValue, int.MinValue };
                for (var j = (int)first; j < first + count; j++)
                {
                    for (var c = 0; c < 9; c++)
                    {
                        want[c % 3] = Math.Min(want[c % 3], (int)Math.Floor(vertices[j][c]));
                        want[3 + c % 3] = Math.Max(want[3 + c % 3], (int)Math.Ceiling(vertices[j][c]));
                    }
                }
                leaves.Add(new LeafInfo(k, (int)first, (int)count));
                end = k + 2;
            }
            else
            {
                var middle = Node(data, k + 1, slots, vertices, leaves, out var a);
                end = Node(data, middle, slots, vertices, leaves, out var b);
                want = new[] { Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]), Math.Max(a[3], b[3]), Math.Max(a[4], b[4]), Math.Max(a[5], b[5]) };
            }
            if (!box.SequenceEqual(want)) throw new InvalidDataException($"Slot {k}'s box doesn't bound what is below it.");
            var skip = w & Skip;
            if (skip != (end == slots ? 0 : 16 * (end - k))) throw new InvalidDataException($"Slot {k} skips {skip} bytes, its subtree ends at slot {end} of {slots}.");
            return end;
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
