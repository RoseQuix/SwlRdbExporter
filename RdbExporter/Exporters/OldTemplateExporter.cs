using RdbExporter.Entities;
using RdbExporter.Parsers;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Old-format spell (1040005) and item (1000020) templates as JSON: lists of stats, name and description, game
    /// actions and criteria. RDBH-encrypted entries are written as their header.
    /// </summary>
    public class OldTemplateExporter : JsonExporter
    {
        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data) => OldTemplateParser.Parse(data);
    }
}
