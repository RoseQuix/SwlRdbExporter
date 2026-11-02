using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Terrain layer height maps (1000069, RDBGroundHeightmap_c), id = terrain layer. Writes the top height mip as an
    /// 8-bit PNG and the normal map baked from it as a BC5 (ATI2) DDS with "_normal" appended.
    /// Layout: u32 1, u32 width, height; each mip largest first down to 1 × 1 as width × height u8 + u32 1; u32 normal
    /// width, height; normal mips largest first down to 4 × 4, each blocks × 6 bytes (an unused green copy) + u32 1 +
    /// blocks × 16 BC5 + u32 1.
    /// </summary>
    public class LayerHeightmapExporter : IExporter
    {
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
            if (r.U32() != 1) throw new InvalidDataException("First u32 isn't 1.");
            int width = (int)r.U32(), height = (int)r.U32();
            int topWidth = width, topHeight = height;
            byte[] topMip = null;
            while (true)
            {
                var mip = r.Bytes(width * height).ToArray();
                topMip ??= mip;
                Marker(r);
                if (width == 1 && height == 1) break;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }

            int normalWidth = (int)r.U32(), normalHeight = (int)r.U32();
            var normalMips = new List<byte[]>();
            for (int w = normalWidth, h = normalHeight; w >= 4 && h >= 4; w /= 2, h /= 2)
            {
                var blocks = w / 4 * (h / 4);
                r.Bytes(blocks * 6);
                Marker(r);
                normalMips.Add(r.Bytes(blocks * 16).ToArray());
                Marker(r);
            }
            r.ExpectEnd();

            var pngPath = Helpers.GetOutputPath(parameters, entry, ".png", true);
            using (var output = File.Create(pngPath))
            {
                PngWriter.WriteGray8(output, topWidth, topHeight, topMip);
            }
            if (normalMips.Count > 0)
            {
                using var output = File.Create(Path.ChangeExtension(pngPath, null) + "_normal.dds");
                DdsWriter.Write(output, normalWidth, normalHeight, "ATI2", normalMips.ToArray());
            }
        }

        private static void Marker(SpanReader r)
        {
            var marker = r.U32();
            if (marker != 1) throw new InvalidDataException($"Mip marker at 0x{r.Position - 4:x} is {marker}, not 1.");
        }
    }
}
