using System;
using RdbExporter.Entities;
using System.IO;
using RdbExporter.Utilities;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Writes entries as they are. Arguments[0] is the extension; with Arguments[1] = "AppendExtension" it's added to real
    /// filenames that end in anything else, for types whose names carry other extensions or dots.
    /// </summary>
    public class RawFileExporter : IExporter
    {
        public void RunExport(ExportParameters parameters)
        {
            if (parameters.Arguments.Count < 1) throw new ArgumentException("RawFileExporter requires extension as first argument.");

            Parallel.ForEach(parameters.RdbFileEntries, (rdbEntry) => Process(parameters, rdbEntry));
        }

        public void Process(ExportParameters parameters, IDBRIndexEntrty entry)
        {
            var appendExtension = parameters.Arguments.Count > 1 && parameters.Arguments[1] == "AppendExtension";
            var outputPath = Helpers.GetOutputPath(parameters, entry, parameters.Arguments[0], appendExtension: appendExtension);

            using var reader = entry.OpenEntryFile(parameters.SwlInstallDir);
            using var writer = File.OpenWrite(outputPath);
            reader.BaseStream.CopyToLimited(writer, entry.FileLength);
        }
    }
}
