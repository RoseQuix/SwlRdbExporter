using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Converts GRNC terrain chunks (1066602, unsigned id = playfield × 100000 + lod × 10000 + y × 100 + x) to OBJ, x = column and
    /// z = row in grid units from the chunk's origin (LOD n vertices are 2^n units apart), y = height × scale, where the
    /// scale (the playfield's GRND height_scale / 256) is recovered from the header's max height. LOD 0 chunks have their
    /// own index buffer, written as one group per texture batch (with its 4 GRND layer slots); other LODs have none, so
    /// a plain grid split is used (an assumption). Each vertex also gets its stored normal.
    /// Layout: u32 size, zlib: "GRNC", u32 version, u16 vertex count, 65 × 65 {u16 height, u8 nx, u8 nz, u8 edge flag}
    /// row by row, then (count − 65²) split vertices {u8 column, u8 row, u16 height, u8 nx, u8 nz, u8 edge}, u16 x, u16 y,
    /// f32 min height, f32 max height, u8 lod; lod 0: u32 tile map key, f32 uv[4], u16 indices[8192 × 3],
    /// u16 batch count, batches × {u8 layers[4], u16 triangle count}.
    /// </summary>
    public class GroundChunkExporter : IExporter
    {
        private const int Grid = 65;
        private const int Triangles = 64 * 64 * 2;

        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    var obj = Convert(entry, Inflate(entry.ReadAllBytes(parameters.SwlInstallDir)));
                    //The index stores ids as int32; chunk ids past 2^31 are named unsigned
                    File.WriteAllText(Path.Combine(parameters.ExportDirectory, $"{(uint)entry.Id}.obj"), obj, new UTF8Encoding(false));
                }
                catch (InvalidDataException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }

        private static byte[] Inflate(byte[] data)
        {
            if (data.Length < 4) throw new InvalidDataException("Too short.");
            var size = BitConverter.ToInt32(data, 0);
            using var input = new MemoryStream(data, 4, data.Length - 4);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            if (output.Length != size) throw new InvalidDataException($"Size prefix {size}, inflated {output.Length}.");
            return output.ToArray();
        }

        private static string Convert(IDBRIndexEntrty entry, byte[] data)
        {
            var r = new SpanReader(data);
            if (!r.Bytes(4).SequenceEqual("GRNC"u8)) throw new InvalidDataException("No GRNC magic.");
            var version = r.U32();
            int count = r.U16();
            if (count < Grid * Grid) throw new InvalidDataException($"{count} vertices, fewer than {Grid * Grid}.");
            var vertices = new (int Column, int Row, int Height, byte Nx, byte Nz)[count];
            for (var k = 0; k < Grid * Grid; k++)
            {
                vertices[k] = (k % Grid, k / Grid, r.U16(), r.U8(), r.U8());
                r.U8();
            }
            for (var k = Grid * Grid; k < count; k++)
            {
                vertices[k] = (r.U8(), r.U8(), r.U16(), r.U8(), r.U8());
                r.U8();
            }
            var x = r.U16();
            var y = r.U16();
            var minHeight = r.F32();
            var maxHeight = r.F32();
            var lod = r.U8();
            uint? key = null;
            float[] uv = null;
            ushort[] indices = null;
            (byte[] Layers, int Triangles)[] batches = null;
            if (lod == 0)
            {
                key = r.U32();
                uv = r.F32s(4);
                indices = new ushort[Triangles * 3];
                for (var i = 0; i < indices.Length; i++) indices[i] = r.U16();
                batches = new (byte[], int)[r.U16()];
                for (var i = 0; i < batches.Length; i++) batches[i] = (r.Bytes(4).ToArray(), r.U16());
            }
            r.ExpectEnd();

            var highest = vertices.Max(v => v.Height);
            var scale = highest > 0 ? maxHeight / highest : 0;
            var step = 1 << lod;
            var obj = new StringBuilder();
            obj.AppendLine($"# GRNC version {version}, chunk {x} {y}, lod {lod}, heights {F(minHeight)} to {F(maxHeight)}");
            if (lod == 0) obj.AppendLine($"# tile map {key}, uv offset {F(uv[0])} {F(uv[1])} scale {F(uv[2])} {F(uv[3])}");
            foreach (var v in vertices) obj.AppendLine($"v {x + v.Column * step} {F(v.Height * (double)scale)} {y + v.Row * step}");
            foreach (var v in vertices)
            {
                double nx = 2 * v.Nx / 255.0 - 1, nz = 2 * v.Nz / 255.0 - 1;
                obj.AppendLine($"vn {F(nx)} {F(Math.Sqrt(Math.Max(0, 1 - nx * nx - nz * nz)))} {F(nz)}");
            }

            void Face(int a, int b, int c)
            {
                if (a >= count || b >= count || c >= count) throw new InvalidDataException($"Index out of range in chunk {entry.Id}.");
                obj.AppendLine($"f {a + 1}//{a + 1} {b + 1}//{b + 1} {c + 1}//{c + 1}");
            }

            if (indices != null)
            {
                var triangle = 0;
                for (var b = 0; b < batches.Length; b++)
                {
                    obj.AppendLine($"g batch{b}");
                    obj.AppendLine($"# layers {string.Join(" ", batches[b].Layers)}");
                    for (var t = 0; t < batches[b].Triangles && triangle < Triangles; t++, triangle++)
                    {
                        Face(indices[3 * triangle], indices[3 * triangle + 1], indices[3 * triangle + 2]);
                    }
                }
                if (triangle < Triangles) obj.AppendLine("g unbatched");
                for (; triangle < Triangles; triangle++) Face(indices[3 * triangle], indices[3 * triangle + 1], indices[3 * triangle + 2]);
            }
            else
            {
                for (var row = 0; row < Grid - 1; row++)
                {
                    for (var col = 0; col < Grid - 1; col++)
                    {
                        var a = row * Grid + col;
                        Face(a, a + Grid, a + 1);
                        Face(a + Grid, a + Grid + 1, a + 1);
                    }
                }
            }
            return obj.ToString();
        }

        private static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
