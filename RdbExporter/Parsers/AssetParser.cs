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
            ["MorphNames"] = MorphNames,
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

        private static readonly Dictionary<uint, string> MorphKinds = new()
        {
            [0] = "FaceMorph", [1] = "BodyMorph", [2] = "TexScript", [3] = "HairMesh", [4] = "BeardMesh", [5] = "PiercingMesh",
            [6] = "TexScript", [8] = "TexScript", [11] = "Looks", [12] = "MorphVector",
        };

        /// <summary>
        /// The character-creator morph value table (1000076, e_RDB_MorphNameIDs, one entry).
        /// u32 count, u32 version (2), then count × {u32 id, u32 n, char name[n], u32 kind, f32 min, f32 max}.
        /// Kinds are named from the data (texscripts are 1010035 ids, looks 1010226).
        /// </summary>
        private static object MorphNames(SpanReader r, LayoutNameLookup name)
        {
            var count = r.U32();
            var version = r.U32();
            if (version != 2) throw new InvalidDataException($"Unsupported version {version}.");
            var values = new List<object>();
            for (; count > 0; count--)
            {
                var id = r.U32();
                var valueName = r.Latin1((int)r.U32());
                var kind = r.U32();
                values.Add(new { Id = id, Name = valueName, Kind = kind, KindName = MorphKinds.GetValueOrDefault(kind), Min = r.F32(), Max = r.F32() });
            }
            return new { Version = version, Values = values };
        }
    }
}
