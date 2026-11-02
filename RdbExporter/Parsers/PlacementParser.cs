using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Per-playfield placement types. Each layout function decodes one entry (after the inner header) to plain
    /// dictionaries and lists for JSON, and calls <c>name(type, id)</c> to look up filenames of referenced RDB entries.
    /// Field names follow the notes; unnamed fields keep the notes' placeholder names (v2a, v4b, ...).
    /// </summary>
    public static class PlacementParser
    {
        public delegate string NameLookup(uint type, uint id);

        public static readonly Dictionary<string, Func<SpanReader, NameLookup, object>> Layouts = new()
        {
            ["Lights"] = Lights,
            ["Dynels"] = Dynels,
            ["Particles"] = Particles,
            ["ScryScripts"] = ScryScripts,
            ["SensorVolumes"] = SensorVolumes,
        };

        public static object Parse(string layout, byte[] data, NameLookup name)
        {
            if (!Layouts.TryGetValue(layout, out var parse)) throw new ArgumentException($"Unknown placement layout '{layout}'.");
            var reader = new SpanReader(data);
            var result = parse(reader, name);
            reader.ExpectEnd();
            return result;
        }

        private static readonly string[] LightTypes = { null, "Point", "Spot", "RectArea" };
        private static readonly string[] FlickerTypes = { "None", "RandomSmooth", "RandomStep", "Sinus" };

        /// <summary>
        /// Light sets (1000049, RDBPointLightSet_t), id = playfield or playfield | 0x40000000.
        /// [i32 -1, u32 version] u32 count (no -1 marker: version 0), then count records:
        ///   u16 n, char name[n]; f32 colour[3]; f32 position[3]; f32 fallOffRange, attenuation, radius
        ///   &gt;= 1: u8 flickering; f32 flickerMaxVar, flickerMinVar, flickerFreq; u32 flickerType; f32 randomSeed
        ///   &gt;= 2: u8 castShadow; f32 v2a; f32 v2b[2]
        ///   &gt;= 3: f32[7] (dropped by the client)
        ///   &gt;= 4: u32 lightType (1 point, 2 spot, 3 rect area); u32 v4b, v4c; f32 fallOffCone, hotSpotCone; f32 v4f[2];
        ///         f32 v4h, v4i, v4j; u32 v4k; f32 rotation[4] (x, y, z, w)
        ///   &gt;= 5: u32 castShadow;  &gt;= 6: u8 v6a; f32 v6b;  &gt;= 7: f32 area[4]
        /// </summary>
        private static object Lights(SpanReader r, NameLookup name)
        {
            var first = r.I32();
            uint version, count;
            if (first == -1)
            {
                version = r.U32();
                count = r.U32();
            }
            else
            {
                version = 0;
                count = (uint)first;
            }

            var records = new List<Dictionary<string, object>>();
            for (var i = 0; i < count; i++)
            {
                var light = new Dictionary<string, object>
                {
                    ["Name"] = r.Latin1(r.U16()),
                    ["Colour"] = r.F32s(3),
                    ["Position"] = r.F32s(3),
                    ["FallOffRange"] = r.F32(),
                    ["Attenuation"] = r.F32(),
                    ["Radius"] = r.F32(),
                    ["Type"] = 1u,
                };
                if (version >= 1)
                {
                    light["Flickering"] = r.U8() != 0;
                    light["FlickerMaxVar"] = r.F32();
                    light["FlickerMinVar"] = r.F32();
                    light["FlickerFreq"] = r.F32();
                    light["FlickerType"] = Enum(FlickerTypes, r.U32());
                    light["RandomSeed"] = r.F32();
                }
                if (version >= 2)
                {
                    light["CastShadow"] = r.U8() != 0;
                    light["v2a"] = r.F32();
                    light["v2b"] = r.F32s(2);
                }
                if (version >= 3) light["v3"] = r.F32s(7);
                if (version >= 4)
                {
                    light["Type"] = r.U32();
                    light["v4b"] = r.U32();
                    light["v4c"] = r.U32();
                    light["FallOffCone"] = r.F32();
                    light["HotSpotCone"] = r.F32();
                    light["v4f"] = r.F32s(2);
                    light["v4h"] = r.F32();
                    light["v4i"] = r.F32();
                    light["v4j"] = r.F32();
                    light["v4k"] = r.U32();
                    light["Rotation"] = r.F32s(4);
                }
                if (version >= 5) light["CastShadow"] = r.U32(); //0, 1 or 3, so not a bool from this version on
                if (version >= 6)
                {
                    light["v6a"] = r.U8();
                    light["v6b"] = r.F32();
                }
                if (version >= 7) light["Area"] = r.F32s(4);
                light["Type"] = Enum(LightTypes, (uint)light["Type"]);
                records.Add(light);
            }
            return new { Version = version, Lights = records };
        }

        /// <summary>
        /// Simple dynels (1000057, GameData::PlayfieldSimpleDynels_c). u32 count, then count records:
        ///   i32 version (1-4; 0 or less: nothing more)
        ///   string name; f32 position[3]; f32 rotation[4] (x, y, z, w); f32 scale; u32 index (-1: none);
        ///   u32 visualType, visualId; &gt;= 2: f32 bounds[6] (local min xyz, max xyz); &gt;= 3: u32 object id (a game
        ///   object of type 55000); &gt;= 4: u8 flag; string tag (4 characters)
        /// </summary>
        private static object Dynels(SpanReader r, NameLookup name)
        {
            var count = r.U32();
            var records = new List<Dictionary<string, object>>();
            for (var i = 0; i < count; i++)
            {
                var version = r.I32();
                var dynel = new Dictionary<string, object> { ["Version"] = version };
                if (version > 0)
                {
                    dynel["Name"] = BinaryStreamString(r);
                    dynel["Position"] = r.F32s(3);
                    dynel["Rotation"] = r.F32s(4);
                    dynel["Scale"] = r.F32();
                    dynel["Index"] = r.I32();
                    var visualType = r.U32();
                    var visualId = r.U32();
                    dynel["VisualType"] = visualType;
                    dynel["VisualId"] = visualId;
                    dynel["VisualName"] = name(visualType, visualId);
                    if (version >= 2) dynel["Bounds"] = r.F32s(6);
                    if (version >= 3) dynel["Object"] = r.U32();
                    if (version >= 4)
                    {
                        dynel["Flag"] = r.U8();
                        dynel["Tag"] = BinaryStreamString(r);
                    }
                }
                records.Add(dynel);
            }
            return new { Dynels = records };
        }

        /// <summary>
        /// Particle effect placements (1000058, GameData::PlayfieldEffects_c), id = playfield or playfield | 0x40000000.
        /// u32 count, then count × {string name; u32 effect (1010207); f32 position[3]; f32 rotation[4] (x, y, z, w); f32 scale}
        /// </summary>
        private static object Particles(SpanReader r, NameLookup name)
        {
            var count = r.U32();
            var records = new List<Dictionary<string, object>>();
            for (var i = 0; i < count; i++)
            {
                var placement = new Dictionary<string, object> { ["Name"] = BinaryStreamString(r) };
                var effect = r.U32();
                placement["Effect"] = effect;
                placement["EffectName"] = name(1010207, effect);
                placement["Position"] = r.F32s(3);
                placement["Rotation"] = r.F32s(4);
                placement["Scale"] = r.F32();
                records.Add(placement);
            }
            return new { Effects = records };
        }

        /// <summary>
        /// ScryScripts (1000504, ScryScriptsData_c). u32 version (1-3), u32 count, then count records:
        ///   f32 position[3] (all zero: none); u32 script (1010028 script group); &gt;= 2: u32 activation radius in metres
        ///   (version 1: 70); &gt;= 3: u32 instance id; u16 n, name; u16 n, description; u16 count × {u16 n, name; u16 n, value}
        /// </summary>
        private static object ScryScripts(SpanReader r, NameLookup name)
        {
            var version = r.U32();
            if (version < 1 || version > 3) throw new InvalidDataException($"Unsupported version {version}.");
            var count = r.U32();
            var records = new List<Dictionary<string, object>>();
            for (var i = 0; i < count; i++)
            {
                var script = new Dictionary<string, object> { ["Position"] = r.F32s(3) };
                var scriptId = r.U32();
                script["Script"] = scriptId;
                script["ScriptName"] = name(1010028, scriptId);
                script["Range"] = version >= 2 ? r.U32() : 70u;
                script["Instance"] = version >= 3 ? r.U32() : 0u;
                script["Name"] = ShortString(r);
                script["Description"] = ShortString(r);
                var variables = new List<object>();
                for (var v = r.U16(); v > 0; v--) variables.Add(new { Name = ShortString(r), Value = ShortString(r) });
                script["Variables"] = variables;
                records.Add(script);
            }
            return new { Version = version, Scripts = records };
        }

        /// <summary>u16 n (at most 10000), char[n].</summary>
        private static string ShortString(SpanReader r)
        {
            var length = r.U16();
            if (length > 10000) throw new InvalidDataException($"Invalid string length {length}.");
            return r.Latin1(length);
        }

        /// <summary>
        /// Sensor volumes (1000055, RDBPlayfieldSensorVolumesData). Array counts are BinaryStream counts.
        ///   u32 version (1); u8 flag
        ///   array volumes {u32 id (ScryScript index &lt;&lt; 16 | index); array f32[3] points; array faces (array u32 point
        ///     indices); string name}
        ///   array {u32 key, u32 value}; array {string name, u32 value}; BoxCluster tree over the volumes
        /// </summary>
        private static object SensorVolumes(SpanReader r, NameLookup name)
        {
            var version = r.U32();
            if (version != 1) throw new InvalidDataException($"Unsupported version {version}.");
            var flag = r.U8();
            var volumes = new List<object>();
            for (var i = BinaryStreamCount(r, 30000); i > 0; i--)
            {
                var id = r.U32();
                var points = new List<float[]>();
                for (var k = BinaryStreamCount(r, 30000); k > 0; k--) points.Add(r.F32s(3));
                var faces = new List<uint[]>();
                for (var k = BinaryStreamCount(r, 30000); k > 0; k--) faces.Add(r.U32s(BinaryStreamCount(r, 30000)));
                volumes.Add(new { Id = id, Name = BinaryStreamString(r), Points = points, Faces = faces });
            }
            var map = new List<uint[]>();
            for (var i = BinaryStreamCount(r, 1000000); i > 0; i--) map.Add(r.U32s(2));
            var names = new List<object>();
            for (var i = BinaryStreamCount(r, 1000000); i > 0; i--) names.Add(new { Name = BinaryStreamString(r), Value = r.U32() });
            return new { Version = version, Flag = flag, Volumes = volumes, Map = map, Names = names, Tree = BoxCluster(r) };
        }

        /// <summary>BoxCluster_t: array children, f32 box[6], u8 isLeaf, [isLeaf] u32 index.</summary>
        private static object BoxCluster(SpanReader r)
        {
            var children = new List<object>();
            for (var i = BinaryStreamCount(r, 30000); i > 0; i--) children.Add(BoxCluster(r));
            var box = r.F32s(6);
            uint? leaf = r.U8() != 0 ? r.U32() : null;
            return new { Box = box, Leaf = leaf, Children = children };
        }

        /// <summary>BinaryStream array count, stored as (n + 1) × 1009.</summary>
        private static int BinaryStreamCount(SpanReader r, int limit)
        {
            var word = r.U32();
            if (word % 1009 != 0 || word / 1009 < 1 || word / 1009 - 1 > limit) throw new InvalidDataException($"Bad count word 0x{word:x} at 0x{r.Position - 4:x}.");
            return (int)(word / 1009 - 1);
        }

        /// <summary>u16 n, char[n]; n = 0xffff is followed by u16 0x1ff and a u32 length (BinaryStream::ReadString).</summary>
        private static string BinaryStreamString(SpanReader r)
        {
            int length = r.U16();
            if (length == 0xffff)
            {
                var marker = r.U16();
                if (marker != 0x1ff) throw new InvalidDataException($"Bad long-string marker 0x{marker:x}.");
                length = (int)r.U32();
            }
            return r.Latin1(length);
        }

        /// <summary>A name from <paramref name="names"/>, or the number itself if it's out of range.</summary>
        private static object Enum(string[] names, uint value) => value < names.Length && names[value] != null ? names[value] : value;
    }
}
