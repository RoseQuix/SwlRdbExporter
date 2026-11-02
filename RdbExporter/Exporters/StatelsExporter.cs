using RdbExporter.Entities;
using RdbExporter.Parsers;
using RdbExporter.Utilities;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Static mesh placements (1000060, one entry per playfield) as JSON, with each visual's filename where the filename
    /// index has one.
    /// </summary>
    public class StatelsExporter : JsonExporter
    {
        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data)
        {
            var statels = StatelsParser.Parse(data);
            var filenames = Helpers.GetFilenameIndex(parameters.SwlInstallDir);
            foreach (var record in statels.Records)
            {
                if (filenames.TryGetValue((int)record.Type, out var names) && names.TryGetValue((int)record.Id, out var name)) record.Name = name;
            }
            return statels;
        }
    }
}
