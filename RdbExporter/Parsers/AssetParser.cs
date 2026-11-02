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
            ["GrassConfig"] = GrassConfig,
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

        /// <summary>
        /// Grass configs (1010220, RDBGrassConfig_c), id = playfield. Most entries are the inner header alone.
        ///   u32 version (&lt; 4); f32 f30, skipped, f34; u32 count, then count layers:
        ///     u32 version (1-4); version &gt; 1: i32 texture (1010217); u32 n, name; u32 u28; f32 f[4];
        ///     &gt;= 2: f32 uv[4] (rect in the 1010218 atlas, v from the top), u32 width, height (else 512 × 256);
        ///     &gt;= 3: f32 colours[8] (bottom rgb, top rgb, bottom and top multipliers);
        ///     4: f32 f4[10], u32 n, n × {i32 mesh (1010221), i32}
        ///   version &gt; 1: u32 hasRegions; if set: u32 regionSize, nx, ny, nx × ny × u32
        /// </summary>
        private static object GrassConfig(SpanReader r, LayoutNameLookup name)
        {
            if (r.Remaining == 0) return new { Empty = true };
            var version = r.U32();
            if (version >= 4) throw new InvalidDataException($"Unsupported version {version}.");
            var config = new Dictionary<string, object> { ["Version"] = version, ["f30"] = r.F32(), ["Skipped"] = r.F32(), ["f34"] = r.F32() };
            var layers = new List<object>();
            for (var count = r.U32(); count > 0; count--)
            {
                var layerVersion = r.U32();
                if (layerVersion > 4) throw new InvalidDataException($"Unsupported layer version {layerVersion}.");
                var layer = new Dictionary<string, object> { ["Version"] = layerVersion };
                var texture = layerVersion == 1 ? -1 : r.I32();
                layer["Texture"] = texture;
                layer["TextureName"] = texture < 0 ? null : name(1010217, (uint)texture);
                layer["Name"] = r.Latin1((int)r.U32());
                layer["u28"] = r.U32();
                layer["f"] = r.F32s(4);
                if (layerVersion >= 2)
                {
                    layer["Uv"] = r.F32s(4);
                    layer["Width"] = r.U32();
                    layer["Height"] = r.U32();
                }
                else
                {
                    layer["Width"] = 512u;
                    layer["Height"] = 256u;
                }
                if (layerVersion >= 3) layer["Colours"] = r.F32s(8);
                if (layerVersion == 4)
                {
                    layer["f4"] = r.F32s(10);
                    var meshes = new List<int[]>();
                    for (var n = r.U32(); n > 0; n--) meshes.Add(new[] { r.I32(), r.I32() });
                    layer["Meshes"] = meshes;
                }
                layers.Add(layer);
            }
            config["Layers"] = layers;
            if (version > 1 && r.U32() != 0)
            {
                config["RegionSize"] = r.U32();
                var nx = r.U32();
                var ny = r.U32();
                config["RegionsX"] = nx;
                config["RegionsY"] = ny;
                config["Regions"] = r.U32s((int)(nx * ny));
            }
            return config;
        }
    }
}
