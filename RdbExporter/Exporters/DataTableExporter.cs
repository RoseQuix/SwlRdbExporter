using RdbExporter.Entities;
using RdbExporter.Parsers;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// The small one-entry data tables (feats, XP, tokens, game tweaks, ...) as JSON.
    /// </summary>
    public class DataTableExporter : JsonExporter
    {
        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data) => DataTableParser.Parse(entry.Type, data);
    }
}
