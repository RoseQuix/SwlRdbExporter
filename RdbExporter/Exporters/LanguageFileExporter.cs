using RdbExporter.Entities;
using RdbExporter.Parsers;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Writes TDL1 language files (1030001) as JSON: the language, its plural rule and each string category's TDC2 table.
    /// </summary>
    public class LanguageFileExporter : JsonExporter
    {
        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data) => TDL1Parser.Parse(data);
    }
}
