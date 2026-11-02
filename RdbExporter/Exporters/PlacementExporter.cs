using RdbExporter.Entities;
using RdbExporter.Parsers;
using RdbExporter.Utilities;
using System;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Per-playfield placement types as JSON. Arguments[0] names the layout in PlacementParser.Layouts.
    /// </summary>
    public class PlacementExporter : JsonExporter
    {
        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data)
        {
            if (parameters.Arguments.Count < 1) throw new ArgumentException("PlacementExporter requires the layout name as first argument.");
            var filenames = Helpers.GetFilenameIndex(parameters.SwlInstallDir);
            return PlacementParser.Parse(parameters.Arguments[0], data,
                (type, id) => filenames.TryGetValue((int)type, out var names) && names.TryGetValue((int)id, out var name) ? name : null);
        }
    }
}
