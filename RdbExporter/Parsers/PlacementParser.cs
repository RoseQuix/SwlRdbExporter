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

        /// <summary>A name from <paramref name="names"/>, or the number itself if it's out of range.</summary>
        private static object Enum(string[] names, uint value) => value < names.Length && names[value] != null ? names[value] : value;
    }
}
