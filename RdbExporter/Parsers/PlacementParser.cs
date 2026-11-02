using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RdbExporter.Parsers
{
    /// <summary>Filename of an RDB entry from the filename index, or null.</summary>
    public delegate string LayoutNameLookup(uint type, uint id);

    /// <summary>
    /// Per-playfield placement types. Each layout function decodes one entry (after the inner header) to plain
    /// dictionaries and lists for JSON, and calls <c>name(type, id)</c> to look up filenames of referenced RDB entries.
    /// LayoutExporter checks that the whole entry was consumed.
    /// Field names follow the notes; unnamed fields keep the notes' placeholder names (v2a, v4b, ...).
    /// </summary>
    public static class PlacementParser
    {
        public static readonly Dictionary<string, Func<SpanReader, LayoutNameLookup, object>> Layouts = new()
        {
            ["Lights"] = Lights,
            ["Dynels"] = Dynels,
            ["Particles"] = Particles,
            ["ScryScripts"] = ScryScripts,
            ["SensorVolumes"] = SensorVolumes,
            ["EnvironmentAreas"] = EnvironmentAreas,
            ["Doors"] = Doors,
            ["Destructibles"] = Destructibles,
            ["BoundedAreaScripts"] = BoundedAreaScripts,
            ["BoundedAreaReverb"] = BoundedAreaReverb,
            ["EnvironmentSettings"] = EnvironmentSettings,
        };

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
        private static object Lights(SpanReader r, LayoutNameLookup name)
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
        private static object Dynels(SpanReader r, LayoutNameLookup name)
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
        private static object Particles(SpanReader r, LayoutNameLookup name)
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
        private static object ScryScripts(SpanReader r, LayoutNameLookup name)
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
        internal static string ShortString(SpanReader r)
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
        private static object SensorVolumes(SpanReader r, LayoutNameLookup name)
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

        /// <summary>
        /// Environment areas (1000059, RDBEnvArea_c). u32 count, then count areas:
        ///   u32 version (1-4); u16 n, name; &gt;= 4: u32 unbounded (1: y range widened to -10000..10000); f32 centre[3]
        ///   (of cell 0, 0, 0); f32 halfSize (cells are 2 × halfSize wide); u32 nx, ny, nz; &gt;= 2: u32 (unused);
        ///   per cell: version &lt; 3: u32 value, else u32 n, n × u32 value. Values are 1060667 environment ids.
        /// </summary>
        private static object EnvironmentAreas(SpanReader r, LayoutNameLookup name)
        {
            var areas = new List<Dictionary<string, object>>();
            for (var count = r.U32(); count > 0; count--)
            {
                var version = r.U32();
                if (version < 1 || version > 4) throw new InvalidDataException($"Unsupported area version {version}.");
                var area = new Dictionary<string, object>
                {
                    ["Version"] = version,
                    ["Name"] = r.Latin1(r.U16()),
                    ["Unbounded"] = version >= 4 ? r.U32() : 0u,
                    ["Centre"] = r.F32s(3),
                    ["HalfSize"] = r.F32(),
                };
                var dimensions = r.U32s(3);
                area["Dimensions"] = dimensions;
                if (version >= 2) area["Unused"] = r.U32();
                var cells = new List<uint[]>();
                for (long i = (long)dimensions[0] * dimensions[1] * dimensions[2]; i > 0; i--)
                {
                    cells.Add(version < 3 ? new[] { r.U32() } : r.U32s((int)r.U32()));
                }
                area["Cells"] = cells;
                areas.Add(area);
            }
            return new { Areas = areas };
        }

        /// <summary>
        /// Doors (1000102, GameData::PlayfieldDoors_c), no inner header. u32 version, u32 count, then count records:
        ///   u16 n, name; u32 object (a game object of type 55000); u16 n, label; u8 flag; u32 a; f32 b;
        ///   f32 position[3]; f32 rotation[4] (x, y, z, w); f32 box[6] (min, max); u32 c, d
        /// </summary>
        private static object Doors(SpanReader r, LayoutNameLookup name)
        {
            var version = r.U32();
            var doors = new List<object>();
            for (var count = r.U32(); count > 0; count--)
            {
                doors.Add(new
                {
                    Name = r.Latin1(r.U16()),
                    Object = r.U32(),
                    Label = r.Latin1(r.U16()),
                    Flag = r.U8(),
                    A = r.U32(),
                    B = r.F32(),
                    Position = r.F32s(3),
                    Rotation = r.F32s(4),
                    Box = r.F32s(6),
                    C = r.U32(),
                    D = r.U32(),
                });
            }
            return new { Version = version, Doors = doors };
        }

        /// <summary>
        /// Destructibles (1000061, DestructibleManager_c): array of 21 × u32 records (the second is a 1010030 .idx visual),
        /// then a BoxCluster tree. Empty in every entry of this build.
        /// </summary>
        private static object Destructibles(SpanReader r, LayoutNameLookup name)
        {
            var destructibles = new List<uint[]>();
            for (var i = BinaryStreamCount(r, 30000); i > 0; i--) destructibles.Add(r.U32s(21));
            return new { Destructibles = destructibles, Tree = BoxCluster(r) };
        }

        /// <summary>
        /// Bounded-area fixed 3D ambience (1040007) and music (1040009), keyed by 1000055 sensor volume.
        /// u32 version, u32 count, then count × {u32 volume; u32 priority (7: always plays); &gt;= 2: u8 group;
        /// u32 n, n × u32 script (indices into the playfield's 1000504 ScryScripts)}
        /// </summary>
        private static object BoundedAreaScripts(SpanReader r, LayoutNameLookup name)
        {
            var version = r.U32();
            var records = new List<object>();
            for (var count = r.U32(); count > 0; count--)
            {
                records.Add(new { Volume = r.U32(), Priority = r.U32(), Group = version >= 2 ? r.U8() : (byte)0, Scripts = r.U32s((int)r.U32()) });
            }
            return new { Version = version, Records = records };
        }

        /// <summary>The sound engine's 85 reverb presets (m_ReverbPresets): FMOD's standard set, then two more sets.</summary>
        private static readonly string[] ReverbPresets =
        {
            "Off", "Generic", "Padded Cell", "Room", "Bathroom", "Living Room", "Stone Room", "Auditorium",
            "Concert Hall", "Cave", "Arena", "Hangar", "Carpeted Hallway", "Hallway", "Stone Corridor", "Alley",
            "Forest", "City", "Mountains", "Quarry", "Plain", "Parking Lot", "Sewer Pipe", "Underwater", "Drugged",
            "Dizzy", "Psychotic", "Empty Stadium", "Full Stadium", "Stadium Tannoy", "Squash Court",
            "Small Swimming Pool", "Large Swimming Pool", "Gymnasium", "Workshop", "School Room", "Practice Room",
            "Outhouse", "Caravan", "Domed Tomb", "Saint Paul's Dome", "Small Pipe", "Long Thin Pipe", "Large Pipe",
            "Resonant Pipe", "Backyard", "Rolling Plains", "Deep Canyon", "Creek", "Valley", "Heaven", "Hell",
            "Memory", "Race Commentator", "Pit Garage", "In-car (Stripped out racer)", "In-car (Sportscar)",
            "In-car (Luxury)", "Full Grandstand", "Empty Grandstand", "Tunnel", "City Streets", "Subway", "Museum",
            "Library", "Underpass", "Abandoned City", "Dusty Box Room", "Chapel", "Small Water Room", "No Reverb",
            "Generic", "Forest", "Canyon", "Mountain", "City", "Arena", "Wood Room Small", "Wood Room Large",
            "Stone Room Small", "Stone Room Large", "Cave Small", "Cave Large", "Throne Room", "Sewers",
        };

        /// <summary>
        /// Bounded-area reverb (1040008), keyed by 1000055 sensor volume. u32 version, u32 count, then
        /// count × {u32 volume; u32 preset (index into ReverbPresets); u8 priority (used from version 2 on, else 0)}
        /// </summary>
        private static object BoundedAreaReverb(SpanReader r, LayoutNameLookup name)
        {
            var version = r.U32();
            var records = new List<object>();
            for (var count = r.U32(); count > 0; count--)
            {
                var volume = r.U32();
                var preset = r.U32();
                var priority = r.U8();
                records.Add(new { Volume = volume, Preset = preset, PresetName = preset < ReverbPresets.Length ? ReverbPresets[preset] : null, Priority = version >= 2 ? priority : 0, StoredPriority = priority });
            }
            return new { Version = version, Records = records };
        }

        /// <summary>
        /// Environment settings (1060668, RDBPlayfieldEnvironmentSettings_c). Field names are the notes' offsets.
        ///   "nEfP", u32 version (2-5); u32 n, n × u32 1060667 environment ids
        ///   environment: f32 a[4]; u8 b4; f32 a5[6]; u32 c[5]; f32 a15[3]; u8 b18; f32 a22[8];
        ///     &gt; 3: u32 a30, str name, f32 a38;  &gt; 4: f32 a19[3]
        ///   u32 sky version: 1: u32 mesh, f32 scale[2], 3 × {u32 id, f32[3], u32 n, n × f32[2]}; 2: str sky dome XML
        ///   optional: u32 1, str precipitation XML
        /// str = u32 n, UTF-8.
        /// </summary>
        private static object EnvironmentSettings(SpanReader r, LayoutNameLookup name)
        {
            if (!r.Bytes(4).SequenceEqual("nEfP"u8)) throw new InvalidDataException("No nEfP magic (old format).");
            var version = r.U32();
            if (version < 2 || version > 5) throw new InvalidDataException($"Unsupported version {version}.");
            var settings = r.U32s((int)r.U32());

            var environment = new Dictionary<string, object>
            {
                ["a"] = r.F32s(4), ["b4"] = r.U8(), ["a5"] = r.F32s(6), ["c"] = r.U32s(5), ["a15"] = r.F32s(3), ["b18"] = r.U8(), ["a22"] = r.F32s(8),
            };
            if (version > 3)
            {
                environment["a30"] = r.U32();
                environment["Name"] = Utf8String(r);
                environment["a38"] = r.F32();
            }
            if (version > 4) environment["a19"] = r.F32s(3);

            var skyVersion = r.U32();
            var sky = new Dictionary<string, object> { ["Version"] = skyVersion };
            if (skyVersion == 1)
            {
                sky["Mesh"] = r.U32();
                sky["Scale"] = r.F32s(2);
                var hours = new List<object>();
                for (var i = 0; i < 3; i++)
                {
                    var id = r.U32();
                    var values = r.F32s(3);
                    var pairs = new List<float[]>();
                    for (var n = r.U32(); n > 0; n--) pairs.Add(r.F32s(2));
                    hours.Add(new { Id = id, Values = values, Pairs = pairs });
                }
                sky["Hours"] = hours;
            }
            else if (skyVersion == 2) sky["Xml"] = Utf8String(r);
            else throw new InvalidDataException($"Unsupported sky version {skyVersion}.");

            string rainSnow = null;
            if (r.Remaining > 0)
            {
                var rainSnowVersion = r.U32();
                if (rainSnowVersion != 1) throw new InvalidDataException($"Unsupported rain/snow version {rainSnowVersion}.");
                rainSnow = Utf8String(r);
            }
            return new { Version = version, Settings = settings, Environment = environment, Sky = sky, RainSnowXml = rainSnow };
        }

        internal static string Utf8String(SpanReader r) => System.Text.Encoding.UTF8.GetString(r.Bytes((int)r.U32()));

        /// <summary>BoxCluster_t: array children, f32 box[6], u8 isLeaf, [isLeaf] u32 index.</summary>
        internal static object BoxCluster(SpanReader r)
        {
            var children = new List<object>();
            for (var i = BinaryStreamCount(r, 30000); i > 0; i--) children.Add(BoxCluster(r));
            var box = r.F32s(6);
            uint? leaf = r.U8() != 0 ? r.U32() : null;
            return new { Box = box, Leaf = leaf, Children = children };
        }

        /// <summary>BinaryStream array count, stored as (n + 1) × 1009.</summary>
        internal static int BinaryStreamCount(SpanReader r, int limit)
        {
            var word = r.U32();
            if (word % 1009 != 0 || word / 1009 < 1 || word / 1009 - 1 > limit) throw new InvalidDataException($"Bad count word 0x{word:x} at 0x{r.Position - 4:x}.");
            return (int)(word / 1009 - 1);
        }

        /// <summary>u16 n, char[n]; n = 0xffff is followed by u16 0x1ff and a u32 length (BinaryStream::ReadString).</summary>
        internal static string BinaryStreamString(SpanReader r)
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
