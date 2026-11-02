using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Single-channel rasters stored as {u32 version, u32 width, u32 height} + width × height texels, row-major.
    /// Arguments[0] is the texel format: "R8" (FAOGen ambient occlusion textures, 1010043) is written as 8-bit greyscale
    /// PNG, "R32F" (rain/snow visibility masks, 1066611: occluder heights, row = z) as a greyscale PFM, which keeps the floats.
    /// </summary>
    public class RasterExporter : IExporter
    {
        public void RunExport(ExportParameters parameters)
        {
            if (parameters.Arguments.Count < 1 || parameters.Arguments[0] is not ("R8" or "R32F")) throw new ArgumentException("RasterExporter requires R8 or R32F as first argument.");
            var isFloat = parameters.Arguments[0] == "R32F";

            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                var data = entry.ReadAllBytes(parameters.SwlInstallDir);
                var width = data.Length >= 12 ? BitConverter.ToInt32(data, 4) : 0;
                var height = data.Length >= 12 ? BitConverter.ToInt32(data, 8) : 0;
                if (width <= 0 || height <= 0 || data.Length != 12 + (long)width * height * (isFloat ? 4 : 1))
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': size doesn't match a {parameters.Arguments[0]} raster.");
                    return;
                }

                using var output = File.Create(Helpers.GetOutputPath(parameters, entry, isFloat ? ".pfm" : ".png", true));
                if (isFloat)
                {
                    WritePfm(output, width, height, data.AsSpan(12));
                }
                else
                {
                    PngWriter.WriteGray8(output, width, height, data.AsSpan(12));
                }
            });
        }

        /// <summary>Greyscale PFM: "Pf", size, -1 (little-endian), then the rows bottom to top.</summary>
        private static void WritePfm(Stream output, int width, int height, ReadOnlySpan<byte> texels)
        {
            output.Write(Encoding.ASCII.GetBytes($"Pf\n{width} {height}\n-1.0\n"));
            for (var y = height - 1; y >= 0; y--)
            {
                output.Write(texels.Slice(y * width * 4, width * 4));
            }
        }
    }
}
