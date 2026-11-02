using RdbExporter.Entities;
using RdbExporter.Parsers;
using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Binary types decoded by a layout function, written as JSON. Arguments[0] names the layout, from
    /// PlacementParser.Layouts (per-playfield types) or AssetParser.Layouts (other small formats). If Arguments[1] is
    /// "KeepInnerHeader", the layout reads the inner header {type, id, version} itself (where there is one).
    /// </summary>
    public class LayoutExporter : JsonExporter
    {
        private static readonly Dictionary<string, Func<SpanReader, LayoutNameLookup, object>> Layouts =
            PlacementParser.Layouts.Concat(AssetParser.Layouts).ToDictionary(kv => kv.Key, kv => kv.Value);

        protected override bool KeepInnerHeader(ExportParameters parameters) => parameters.Arguments.Count > 1 && parameters.Arguments[1] == "KeepInnerHeader";

        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data)
        {
            if (parameters.Arguments.Count < 1 || !Layouts.TryGetValue(parameters.Arguments[0], out var parse)) throw new ArgumentException("LayoutExporter requires a known layout name as first argument.");
            var filenames = Helpers.GetFilenameIndex(parameters.SwlInstallDir);
            var reader = new SpanReader(data);
            var result = parse(reader, (type, id) => filenames.TryGetValue((int)type, out var names) && names.TryGetValue((int)id, out var name) ? name : null);
            reader.ExpectEnd();
            return result;
        }
    }
}
