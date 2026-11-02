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
            ["GrassTextureToMesh"] = GrassTextureToMesh,
            ["SoundSet"] = SoundSet,
            ["OldPlayfield"] = OldPlayfield,
            ["TextureDescriptor"] = TextureDescriptor,
            ["AoAtlas"] = AoAtlas,
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

        /// <summary>
        /// GrassTextureToMesh (1010303, one entry): u32 version (1), u32 count, count × {i32 texture (1010217), i32 mesh (1010221), i32 key}.
        /// </summary>
        private static object GrassTextureToMesh(SpanReader r, LayoutNameLookup name)
        {
            var version = r.U32();
            if (version != 1) throw new InvalidDataException($"Unsupported version {version}.");
            var rows = new List<object>();
            for (var count = r.U32(); count > 0; count--)
            {
                var texture = r.I32();
                rows.Add(new { Texture = texture, TextureName = name(1010217, (uint)texture), Mesh = r.I32(), Key = r.I32() });
            }
            return new { Version = version, Rows = rows };
        }

        /// <summary>
        /// Sound sets (1020001, RDBSoundSet_c): pairs of {u32 sound (1020002), u32 flag (read but unused)} to the end.
        /// None of the member sounds exists in this build.
        /// </summary>
        private static object SoundSet(SpanReader r, LayoutNameLookup name)
        {
            if (r.Remaining % 8 != 0) throw new InvalidDataException($"Size {r.Remaining} is not a multiple of 8.");
            var members = new List<object>();
            while (r.Remaining > 0) members.Add(new { Sound = r.U32(), Flag = r.U32() });
            return new { Members = members };
        }

        /// <summary>
        /// Old Age of Conan playfield descriptors (1000001, never read by SWL; field names are guesses from the data).
        ///   u32 version (9-14), u32 id; v9: char name[32]; else u32 flag, str name, str author
        ///   &gt;= 11: u32 sizeX, sizeZ (metres); &gt;= 12: i32 refA; u32 refB, cellSize, cellCount; &gt;= 12: str date;
        ///   &gt;= 14: f32[4]; cellCount × u32 cells; 56 bytes; u32 n, n bytes. str = u32 n, char[n].
        /// </summary>
        private static object OldPlayfield(SpanReader r, LayoutNameLookup name)
        {
            var version = r.U32();
            if (version < 9 || version > 14) throw new InvalidDataException($"Unsupported version {version}.");
            var playfield = new Dictionary<string, object> { ["Version"] = version, ["Id"] = r.U32() };
            if (version == 9)
            {
                var raw = r.Latin1(32);
                playfield["Name"] = raw.Split('\0')[0];
            }
            else
            {
                playfield["Flag"] = r.U32();
                playfield["Name"] = r.Latin1((int)r.U32());
                playfield["Author"] = r.Latin1((int)r.U32());
            }
            if (version >= 11) playfield["Size"] = r.U32s(2);
            if (version >= 12) playfield["RefA"] = r.I32();
            playfield["RefB"] = r.U32();
            playfield["CellSize"] = r.U32();
            var cellCount = r.U32();
            if (cellCount > 1 << 20) throw new InvalidDataException($"Cell count {cellCount}.");
            if (version >= 12) playfield["Date"] = r.Latin1((int)r.U32());
            if (version >= 14) playfield["f"] = r.F32s(4);
            playfield["Cells"] = r.U32s((int)cellCount);
            playfield["Block"] = Convert.ToHexString(r.Bytes(56));
            playfield["Tail"] = Convert.ToHexString(r.Bytes((int)r.U32()));
            return playfield;
        }

        /// <summary>
        /// Texture descriptors (1010040, never read by the client; field names are guesses). u32 version (1), u16 width,
        /// u16 height, u8 mip count, u8 slot count, then slots × {u32 id, i32, u32, u8 slot, u8 flag}. No slot id exists.
        /// </summary>
        private static object TextureDescriptor(SpanReader r, LayoutNameLookup name)
        {
            var descriptor = new Dictionary<string, object> { ["Version"] = r.U32(), ["Width"] = r.U16(), ["Height"] = r.U16(), ["Mips"] = r.U8() };
            var slots = new List<object>();
            for (var count = r.U8(); count > 0; count--) slots.Add(new { Id = r.U32(), A = r.I32(), B = r.U32(), Slot = r.U8(), Flag = r.U8() });
            descriptor["Slots"] = slots;
            return descriptor;
        }

        /// <summary>
        /// FAOGen ambient occlusion atlas descriptors (1010039), id = playfield. Version 6 is what the client reads:
        ///   u32 6, meshGroups, terrainGroups, meshCount, terrainCount
        ///   groups × {u32 count, tile width, tile height, texture width, texture height, texture (1010043)}
        ///   meshes × {u64 instance, u32 fme (1010001), submesh, group, slot}
        ///   terrain × {u32 t0, t1, group (indexes the terrain groups, after the mesh groups), t3}
        /// Version 5 (rejected by the client) keeps its textures inline; their texels are written as base64.
        ///   u32 5, meshGroups, terrainCount, meshCount, texelBytes, groupCount
        ///   meshes × {u64 instance, u32 fme, submesh, vertexCount, texels, group, slot, width, height}
        ///   groups × {u32 count, tile width, tile height, texture width, texture height}, then each group's texels,
        ///   then terrainCount × {u32 width, height, texels}
        /// An instance's tile starts at column slot % (texture width / tile width), row slot / that.
        /// </summary>
        private static object AoAtlas(SpanReader r, LayoutNameLookup name)
        {
            var version = r.U32();
            if (version == 6)
            {
                var meshGroups = r.U32();
                var terrainGroups = r.U32();
                var meshCount = r.U32();
                var terrainCount = r.U32();
                var groups = new List<object>();
                for (var i = 0; i < meshGroups + terrainGroups; i++)
                {
                    groups.Add(new { Count = r.U32(), TileWidth = r.U32(), TileHeight = r.U32(), TextureWidth = r.U32(), TextureHeight = r.U32(), Texture = r.U32() });
                }
                var meshes = new List<object>();
                for (var i = 0; i < meshCount; i++)
                {
                    var instance = r.U64();
                    var fme = r.U32();
                    meshes.Add(new { Instance = instance, Fme = fme, FmeName = name(1010001, fme), Submesh = r.U32(), Group = r.U32(), Slot = r.U32() });
                }
                var terrain = new List<object>();
                for (var i = 0; i < terrainCount; i++) terrain.Add(new { T0 = r.U32(), T1 = r.U32(), Group = r.U32() + meshGroups, T3 = r.U32() });
                return new { Version = version, MeshGroups = meshGroups, Groups = groups, Meshes = meshes, Terrain = terrain };
            }
            if (version == 5)
            {
                var meshGroups = r.U32();
                var terrainCount = r.U32();
                var meshCount = r.U32();
                var texelBytes = r.U32();
                var groupCount = r.U32();
                var meshes = new List<object>();
                for (var i = 0; i < meshCount; i++)
                {
                    var instance = r.U64();
                    var fme = r.U32();
                    meshes.Add(new { Instance = instance, Fme = fme, FmeName = name(1010001, fme), Submesh = r.U32(), VertexCount = r.U32(), Texels = r.U32(), Group = r.U32(), Slot = r.U32(), Width = r.U32(), Height = r.U32() });
                }
                var groupSizes = new List<uint[]>();
                for (var i = 0; i < groupCount; i++) groupSizes.Add(r.U32s(5));
                long total = 0;
                foreach (var g in groupSizes) total += (long)g[3] * g[4];
                if (total != texelBytes) throw new InvalidDataException("Version 5 texel byte count doesn't match the groups.");
                var groups = new List<object>();
                foreach (var g in groupSizes)
                {
                    groups.Add(new { Count = g[0], TileWidth = g[1], TileHeight = g[2], TextureWidth = g[3], TextureHeight = g[4], Texels = r.Bytes((int)(g[3] * g[4])).ToArray() });
                }
                var terrain = new List<object>();
                for (var i = 0; i < terrainCount; i++)
                {
                    var width = r.U32();
                    var height = r.U32();
                    terrain.Add(new { Width = width, Height = height, Texels = r.Bytes((int)(width * height)).ToArray() });
                }
                return new { Version = version, MeshGroups = meshGroups, Groups = groups, Meshes = meshes, Terrain = terrain };
            }
            throw new InvalidDataException($"Unsupported version {version}.");
        }
    }
}
