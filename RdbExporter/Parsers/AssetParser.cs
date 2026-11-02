using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Small binary formats other than the per-playfield types, decoded like PlacementParser's layouts.
    /// </summary>
    public static class AssetParser
    {
        public static readonly Dictionary<string, Func<SpanReader, LayoutNameLookup, object>> Layouts = new()
        {
            ["Attractors"] = Attractors,
        };

        /// <summary>
        /// Attractor sets (1010031, RDBAttractorSet_t), a mesh's named attachment points (.idx &lt;Attractors&gt;).
        /// u32 version (0x10000), u32 count, then count × {u32 n, char name[n]; f32 rotation[4] (normalised on load;
        /// component order unconfirmed); f32 position[3]}
        /// </summary>
        private static object Attractors(SpanReader r, LayoutNameLookup name)
        {
            var version = r.U32();
            if (version != 0x10000) throw new InvalidDataException($"Unsupported version 0x{version:x}.");
            var attractors = new List<object>();
            for (var count = r.U32(); count > 0; count--)
            {
                attractors.Add(new { Name = r.Latin1((int)r.U32()), Rotation = r.F32s(4), Position = r.F32s(3) });
            }
            return new { Attractors = attractors };
        }
    }
}
