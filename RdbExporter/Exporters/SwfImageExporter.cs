using System.Threading.Tasks;
using RdbExporter.Entities;
using SkiaSharp;
using RdbExporter.Parsers;
using System.IO;
using System;
using System.Diagnostics;

namespace RdbExporter.Exporters
{
    public class SwfImageExporter : IExporter
    {
        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, rdbEntry =>
            {
                try
                {
                    var i = 0;
                    foreach (var image in SwfImageParser.ParseImagesFromSwfFile(rdbEntry.OpenEntryFile(parameters.SwlInstallDir).BaseStream))
                    {
                        string filename = rdbEntry.Id + (i++ > 0 ? "-" + i : "") + ".png";
                        using (var output = File.Create(Path.Combine(parameters.ExportDirectory, filename)))
                        {
                            image.Encode(output, SKEncodedImageFormat.Png, 100);
                        }
                        image.Dispose();
                    }
                }
                catch (ArgumentException)
                {
                    Debug.WriteLine($"Failed to parse images from RDB ID '{rdbEntry.Id}'");
                }
                catch (InvalidDataException)
                {
                    Debug.WriteLine($"RDB ID '{rdbEntry.Id}' is not a vaild SWF File.");
                }
            });
        }
    }
}
