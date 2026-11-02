using System;
using RdbExporter.Entities;
using System.IO;
using RdbExporter.Utilities;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    public class RawFileExporter : IExporter
    {
        public void RunExport(ExportParameters parameters)
        {
            if (parameters.Arguments.Count < 1) throw new ArgumentException("RawFileExporter requires extension as first argument.");

            Parallel.ForEach(parameters.RdbFileEntries, (rdbEntry) => Process(parameters, rdbEntry));
        }

        public void Process(ExportParameters parameters, IDBRIndexEntrty entry)
        {
            var outputPath = Helpers.GetOutputPath(parameters, entry, parameters.Arguments[0]);

            using var reader = entry.OpenEntryFile(parameters.SwlInstallDir);
            using var writer = File.OpenWrite(outputPath);
            reader.BaseStream.CopyToLimited(writer, entry.FileLength);
        }
    }
}
