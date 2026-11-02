using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Converts GHM1 heightmaps (1066604, one per playfield, id = playfield) to 16-bit greyscale PNG, row = z and
    /// column = x, plus a JSON file with the scale: world height = sample × Scale / 256.
    /// Layout: "GHM1", u32 width, u32 height, f32 scale, width × height u16, then two f32 (1.0 in every entry, meaning
    /// unknown).
    /// </summary>
    public class HeightmapExporter : IExporter
    {
        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                var data = entry.ReadAllBytes(parameters.SwlInstallDir);
                if (!data.AsSpan().StartsWith("GHM1"u8))
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}' is not a GHM1 heightmap.");
                    return;
                }

                var width = BitConverter.ToInt32(data, 4);
                var height = BitConverter.ToInt32(data, 8);
                var scale = BitConverter.ToSingle(data, 12);
                var end = 16 + 2 * width * height;
                if (end > data.Length)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}' is shorter than its {width} × {height} samples.");
                    return;
                }
                var samples = MemoryMarshal.Cast<byte, ushort>(data.AsSpan(16, 2 * width * height));

                var pngPath = Helpers.GetOutputPath(parameters, entry, ".png", true);
                using (var output = File.Create(pngPath))
                {
                    PngWriter.WriteGray16(output, width, height, samples);
                }

                var info = new
                {
                    Width = width,
                    Height = height,
                    Scale = scale,
                    Trailing = end + 8 <= data.Length ? new[] { BitConverter.ToSingle(data, end), BitConverter.ToSingle(data, end + 4) } : null,
                };
                File.WriteAllText(Path.ChangeExtension(pngPath, ".json"), JsonSerializer.Serialize(info, JsonExporter.JsonOptions));
            });
        }
    }
}
