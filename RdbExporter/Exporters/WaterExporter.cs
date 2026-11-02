using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Water data (1000054, RDBWater_c), id = playfield: a 512 × 512 grid (row = z) of shore mask and foam coordinates
    /// for the water shader, then a flat portal mesh. Writes
    ///   &lt;id&gt;_a.png, _mask.png, _c.png, _d.png   the four u16 cell fields as 16-bit PNGs (the mask is uploaded as mask / 65535;
    ///                                       a looks height-like, c and d are non-zero near shores; meanings unconfirmed)
    ///   &lt;id&gt;_foam.pfm                        foam coordinates (across, along the shoreline, 0) as an RGB float map
    ///   &lt;id&gt;_portal.obj                      the portal mesh
    ///   &lt;id&gt;.json                            the header
    /// Layout: inner header {1000054, id, 1}; u32 version (4, or outdated 1); u32 size tag (0x40002c / 0x400028);
    /// f32 heightScale, cellsPerX, cellsPerZ, invX, invZ, offsetX, offsetZ, f7; 512 × 512 × {u16 a, mask, c, d; f32 across, along};
    /// version 4: u32 (1), u32 index count, u32 vertex count, vertices × 3 f32, indices × u32.
    /// </summary>
    public class WaterExporter : IExporter
    {
        private const int Size = 512;

        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    Process(parameters, entry);
                }
                catch (InvalidDataException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }

        private static void Process(ExportParameters parameters, IDBRIndexEntrty entry)
        {
            var r = new SpanReader(entry.ReadAllBytes(parameters.SwlInstallDir));
            var version = r.U32();
            var sizeTag = r.U32();
            if (!(version == 4 && sizeTag == 0x40002c) && !(version == 1 && sizeTag == 0x400028)) throw new InvalidDataException($"Version {version} with size tag 0x{sizeTag:x}.");
            var header = r.F32s(8);

            var fields = new ushort[4][];
            for (var f = 0; f < 4; f++) fields[f] = new ushort[Size * Size];
            var foam = new float[Size * Size * 3];
            for (var i = 0; i < Size * Size; i++)
            {
                for (var f = 0; f < 4; f++) fields[f][i] = r.U16();
                foam[3 * i] = r.F32();
                foam[3 * i + 1] = r.F32();
            }

            float[] vertices = Array.Empty<float>();
            uint[] indices = Array.Empty<uint>();
            uint? unknown = null;
            if (version == 4)
            {
                unknown = r.U32();
                var indexCount = (int)r.U32();
                var vertexCount = (int)r.U32();
                vertices = r.F32s(vertexCount * 3);
                indices = r.U32s(indexCount);
            }
            r.ExpectEnd();

            var basePath = Path.ChangeExtension(Helpers.GetOutputPath(parameters, entry, ".json", true), null);
            var names = new[] { "a", "mask", "c", "d" };
            for (var f = 0; f < 4; f++)
            {
                using var output = File.Create($"{basePath}_{names[f]}.png");
                PngWriter.WriteGray16(output, Size, Size, fields[f]);
            }
            using (var output = File.Create($"{basePath}_foam.pfm"))
            {
                PfmWriter.Write(output, Size, Size, 3, foam);
            }
            if (vertices.Length > 0)
            {
                var obj = new StringBuilder();
                for (var i = 0; i < vertices.Length; i += 3) obj.AppendLine($"v {F(vertices[i])} {F(vertices[i + 1])} {F(vertices[i + 2])}");
                for (var i = 0; i + 2 < indices.Length; i += 3) obj.AppendLine($"f {indices[i] + 1} {indices[i + 1] + 1} {indices[i + 2] + 1}");
                File.WriteAllText($"{basePath}_portal.obj", obj.ToString());
            }

            var info = new
            {
                Version = version,
                HeightScale = header[0],
                CellsPerX = header[1],
                CellsPerZ = header[2],
                InvX = header[3],
                InvZ = header[4],
                OffsetX = header[5],
                OffsetZ = header[6],
                F7 = header[7],
                Unknown = unknown,
                PortalVertices = vertices.Length / 3,
                PortalTriangles = indices.Length / 3,
            };
            File.WriteAllText(basePath + ".json", JsonSerializer.Serialize(info, JsonExporter.JsonOptions));
        }

        private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
