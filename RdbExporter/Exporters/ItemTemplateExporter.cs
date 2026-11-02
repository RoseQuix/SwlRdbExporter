using RdbExporter.Entities;
using RdbExporter.Parsers;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Item (1000620) and spell (1000621) templates as JSON: stats, game action lists, criteria, name and description.
    /// </summary>
    public class ItemTemplateExporter : JsonExporter
    {
        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data) => ItemTemplateParser.Parse(data);
    }
}
