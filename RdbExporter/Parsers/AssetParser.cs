using RdbExporter.Utilities;
using System;
using System.Collections.Generic;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Small binary formats other than the per-playfield types, decoded like PlacementParser's layouts.
    /// </summary>
    public static class AssetParser
    {
        public static readonly Dictionary<string, Func<SpanReader, LayoutNameLookup, object>> Layouts = new()
        {
        };
    }
}
