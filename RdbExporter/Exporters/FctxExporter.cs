using RdbExporter.Entities;
using RdbExporter.Parsers;
using RdbExporter.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Converts FCTX textures to DDS, one file per layer. The first layer gets the entry's name, later layers get a
    /// suffix (the ATI1 layer that follows a MIXD normal map is the gloss map). Entries that aren't FCTX (1010004 also
    /// holds plain TGA, PNG and DDS files) are written as they are.
    /// </summary>
    public class FctxExporter : IExporter
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
            var data = entry.ReadAllBytes(parameters.SwlInstallDir);
            if (!FctxParser.IsFctx(data))
            {
                File.WriteAllBytes(Helpers.GetOutputPath(parameters, entry, GuessExtension(data), true), data);
                return;
            }

            var fctx = FctxParser.Parse(data);
            var basePath = Helpers.GetOutputPath(parameters, entry, ".dds", true);
            for (var i = 0; i < fctx.Layers.Count; i++)
            {
                var layer = fctx.Layers[i];
                var path = i == 0 ? basePath : Path.ChangeExtension(basePath, null) + LayerSuffix(layer) + ".dds";
                var fourCC = layer.FourCC == "MIXD" ? "ATI2" : layer.FourCC;
                using var output = File.Create(path);
                DdsWriter.Write(output, fctx.Width, fctx.Height, fourCC, layer.Mips);
            }
        }

        private static string LayerSuffix(FctxLayer layer) => layer.FourCC switch
        {
            "ATI1" => "_gloss",
            "MIXD" or "ATI2" => "_normal",
            _ => "_" + layer.FourCC.ToLowerInvariant(),
        };

        private static string GuessExtension(byte[] data)
        {
            var span = data.AsSpan();
            if (span.StartsWith("DDS "u8)) return ".dds";
            if (span.Length >= 4 && span[0] == 0x89 && span[1..].StartsWith("PNG"u8)) return ".png";
            return ".tga";
        }
    }
}
