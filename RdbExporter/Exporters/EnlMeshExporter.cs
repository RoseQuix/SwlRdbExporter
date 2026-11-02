using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Converts .enl meshes (1010036, presumably Enlighten target meshes; the client never reads them) to OBJ, one group
    /// per block. Blocks run to the end: u32 1, u32 n, n × {f32 x, y, z, u, v}, u32 t, t × 3 u16 indices.
    /// </summary>
    public class EnlMeshExporter : IExporter
    {
        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    var r = new SpanReader(entry.ReadAllBytes(parameters.SwlInstallDir));
                    var obj = new StringBuilder();
                    var vertexBase = 1;
                    for (var block = 0; r.Remaining > 0; block++)
                    {
                        if (r.U32() != 1) throw new InvalidDataException($"Bad block marker at 0x{r.Position - 4:x}.");
                        var vertexCount = (int)r.U32();
                        obj.AppendLine($"g block{block}");
                        var vertices = r.F32s(vertexCount * 5);
                        for (var i = 0; i < vertices.Length; i += 5) obj.AppendLine($"v {F(vertices[i])} {F(vertices[i + 1])} {F(vertices[i + 2])}");
                        for (var i = 0; i < vertices.Length; i += 5) obj.AppendLine($"vt {F(vertices[i + 3])} {F(1 - vertices[i + 4])}");
                        for (var t = r.U32(); t > 0; t--)
                        {
                            int a = r.U16() + vertexBase, b = r.U16() + vertexBase, c = r.U16() + vertexBase;
                            obj.AppendLine($"f {a}/{a} {b}/{b} {c}/{c}");
                        }
                        vertexBase += vertexCount;
                    }
                    File.WriteAllText(Helpers.GetOutputPath(parameters, entry, ".obj", true), obj.ToString());
                }
                catch (InvalidDataException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }

        private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
