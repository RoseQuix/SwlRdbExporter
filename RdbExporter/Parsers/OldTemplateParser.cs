using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Old-format templates: spells (1040005, the fallback for 1000621) and items (1000020, the fallback for 1000620),
    /// as Beholder_t::ReadLists reads them. Counts are stored as (n + 1) × 1009.
    ///   u32 kind (&gt;= 10000), u32 list count (&lt;= 1000), lists of {u32 type, u32 entry, body}:
    ///     (15, 23)  stat list: count, count × {u32 stat, u32 value}
    ///     (21, *)   u16 name length, u16 description length, name, description
    ///     (2, *)    GameActionList: count (&lt; 1000), count × GameAction v4
    ///     (22, *)   CriteriaMap_t: count (&lt; 1000), count × {u32 key, count (&lt; 30001), count × criterion}
    ///     (6, *)    no body
    ///   GameAction v4: u32 action, u32 unknown, u32 version (4), u32 n (&lt;= 1000), n × criterion, u32 × 4 (stats 725, 726,
    ///     754, 757), then one value per slot of the action type's schema (u32, or a u16-length string for stat 1241 / 1242).
    ///   criterion = {i32, i32, i32 operator (&lt; 0x402)}
    /// The stat values aren't tagged: their slots come from the exe's per-action-type table (<see cref="Schema"/>, from the
    /// notes' gameaction-v4-schema.json, entries remapped like GameAction version 5 stats). Some entries were written with
    /// a different schema for some action types, so the parser backtracks over the number of values per action until the
    /// whole entry is consumed; values beyond the schema have no stat and are read as u32, or as a string where one plainly
    /// starts. Template.Delta lists each action's value count minus the schema's when any differs.
    /// Most item entries are "RDBH": TEA-encrypted with a per-template key the server sends, so only their header is read.
    /// </summary>
    public static class OldTemplateParser
    {
        private const int MaxValues = 24;
        private static readonly uint[] FixedStats = { 725, 726, 754, 757 };
        private static readonly uint[] StringStats = { 1241, 1242 };

        private static readonly Dictionary<uint, uint[]> Schema = new()
        {
            [53002] = new uint[] { 0, 2, 37, 71 }, [53003] = new uint[] { 194, 39 },
            [53004] = new uint[] { 209, 86, 138, 28, 29, 30 }, [53005] = Array.Empty<uint>(),
            [53006] = Array.Empty<uint>(), [53007] = new uint[] { 0, 39, 1, 71 }, [53008] = new uint[] { 12 },
            [53009] = new uint[] { 187, 248, 245, 243, 254, 174, 66 }, [53010] = new uint[] { 209, 39 },
            [53011] = new uint[] { 17, 18, 19, 20, 11 }, [53012] = new uint[] { 0, 39, 24 },
            [53013] = new uint[] { 0, 39, 66, 71 }, [53014] = new uint[] { 0, 39, 11, 46, 25 },
            [53015] = new uint[] { 187 }, [53016] = new uint[] { 28, 29, 30, 151, 31 },
            [53018] = new uint[] { 88, 2, 11 }, [53019] = new uint[] { 39 }, [53020] = Array.Empty<uint>(),
            [53021] = Array.Empty<uint>(), [53022] = new uint[] { 193, 194, 195, 196, 197, 198, 199, 200, 201, 39 },
            [53023] = new uint[] { 44 }, [53024] = new uint[] { 66 }, [53025] = new uint[] { 66 },
            [53026] = new uint[] { 55, 0, 39, 1305 }, [53027] = new uint[] { 39, 209 },
            [53028] = new uint[] { 76, 243, 194, 8, 86, 39, 73, 198, 199, 200 }, [53029] = new uint[] { 187, 39, 55 },
            [53030] = new uint[] { 187, 39, 55 }, [53031] = new uint[] { 187, 39 }, [53032] = new uint[] { 187, 11 },
            [53033] = new uint[] { 66, 67 }, [53034] = Array.Empty<uint>(), [53035] = Array.Empty<uint>(),
            [53036] = new uint[] { 209 }, [53037] = new uint[] { 66, 67, 68, 69, 163, 39 },
            [53038] = new uint[] { 77, 84, 88, 25, 66, 67, 68 }, [53040] = new uint[] { 39 },
            [53041] = new uint[] { 174, 175, 0, 207 }, [53042] = new uint[] { 39 }, [53043] = new uint[] { 243 },
            [53044] = new uint[] { 187, 248, 238, 11, 243, 254, 170, 93, 250, 66, 67, 174, 175 },
            [53045] = new uint[] { 0, 39 }, [53046] = new uint[] { 0, 70, 33, 34, 88 },
            [53047] = new uint[] { 204, 249, 191 }, [53048] = new uint[] { 209 },
            [53049] = new uint[] { 47, 48, 39, 72, 73, 28, 29, 30, 77 }, [53050] = Array.Empty<uint>(),
            [53051] = new uint[] { 39 }, [53052] = new uint[] { 58, 56, 8 }, [53053] = new uint[] { 191 },
            [53055] = new uint[] { 56, 8 }, [53056] = Array.Empty<uint>(), [53057] = new uint[] { 209, 39 },
            [53058] = new uint[] { 77, 39, 88, 175 }, [53059] = new uint[] { 9, 10, 31, 100 },
            [53060] = new uint[] { 39, 25 }, [53061] = new uint[] { 39 }, [53062] = new uint[] { 1, 0, 39, 175 },
            [53063] = new uint[] { 77, 84, 72, 73, 28, 29, 30, 103 },
            [53064] = new uint[] { 1110, 1335, 1336, 1120, 1119, 1337, 795 },
            [53065] = new uint[] { 88, 0, 39, 175, 1305 }, [53066] = new uint[] { 88 },
            [53067] = new uint[] { 0, 39 }, [53068] = new uint[] { 0, 174, 172, 175, 173 },
            [53069] = new uint[] { 1, 39 }, [53070] = new uint[] { 88, 236, 1, 117, 237, 73 },
            [53071] = new uint[] { 31, 90, 91 }, [53072] = new uint[] { 17 },
            [53073] = new uint[] { 0, 2, 37, 71, 11 }, [53074] = new uint[] { 1, 11 },
            [53075] = new uint[] { 1, 39, 55 }, [53076] = new uint[] { 1, 0, 39, 175, 1305 },
            [53077] = new uint[] { 1, 39 }, [53078] = new uint[] { 39 }, [53079] = new uint[] { 39 },
            [53080] = new uint[] { 39, 64 }, [53081] = Array.Empty<uint>(),
            [53082] = new uint[] { 94, 95, 96, 97, 98, 99 }, [53083] = new uint[] { 94, 95, 96, 97, 98, 99, 100 },
            [53084] = new uint[] { 88, 238, 39, 170, 93, 250, 66, 67, 174 }, [53085] = Array.Empty<uint>(),
            [53086] = Array.Empty<uint>(), [53087] = new uint[] { 88, 11 },
            [53088] = new uint[] { 88, 11, 170, 93, 250 }, [53089] = new uint[] { 88, 39 },
            [53090] = Array.Empty<uint>(), [53091] = new uint[] { 39 }, [53092] = new uint[] { 39, 11 },
            [53093] = new uint[] { 39, 72, 66, 67 }, [53094] = Array.Empty<uint>(), [53095] = Array.Empty<uint>(),
            [53096] = new uint[] { 88, 11, 170, 93, 250, 66, 67, 138, 212, 2 }, [53097] = new uint[] { 66, 67 },
            [53098] = new uint[] { 88, 0, 39, 175, 1305 }, [53099] = new uint[] { 1, 0, 39, 175, 1305 },
            [53100] = new uint[] { 239, 240, 66 }, [53101] = new uint[] { 242, 240, 66 },
            [53102] = new uint[] { 241, 240, 66 }, [53103] = new uint[] { 66 }, [53104] = new uint[] { 66 },
            [53105] = new uint[] { 84, 116, 39 }, [53106] = Array.Empty<uint>(), [53107] = new uint[] { 66 },
            [53108] = new uint[] { 1, 39, 187 }, [53109] = new uint[] { 1, 39, 187, 66 },
            [53110] = new uint[] { 0, 39, 1305 }, [53111] = new uint[] { 0, 39 },
            [53112] = new uint[] { 187, 66, 248, 67, 39, 1 }, [53113] = new uint[] { 1, 193, 174, 175, 1307, 1305 },
            [53114] = new uint[] { 187 }, [53115] = new uint[] { 209, 39, 117 }, [53116] = new uint[] { 39 },
            [53117] = new uint[] { 39, 205, 174, 175 }, [53118] = new uint[] { 88, 66, 39, 174, 175 },
            [53119] = new uint[] { 39 }, [53120] = new uint[] { 0, 39, 1305 }, [53121] = new uint[] { 66, 163, 67 },
            [53122] = Array.Empty<uint>(), [53123] = new uint[] { 39, 174 }, [53125] = new uint[] { 39 },
            [53128] = new uint[] { 66, 39 }, [53129] = Array.Empty<uint>(), [53130] = new uint[] { 31, 72, 66 },
            [53131] = new uint[] { 88, 0, 39, 1305 }, [53132] = new uint[] { 209, 210, 39 },
            [53133] = new uint[] { 1, 0, 39, 1305 }, [53134] = new uint[] { 209, 210, 39 },
            [53135] = new uint[] { 66, 39 }, [53136] = new uint[] { 66, 67, 39, 1 },
            [53137] = new uint[] { 209, 86, 28, 29, 30 }, [53138] = Array.Empty<uint>(),
            [53139] = new uint[] { 0, 39 }, [53140] = new uint[] { 0, 39 }, [53141] = new uint[] { 0, 39 },
            [53144] = Array.Empty<uint>(), [53148] = new uint[] { 0, 39 }, [53150] = Array.Empty<uint>(),
            [53151] = new uint[] { 1206, 1207 }, [53152] = new uint[] { 39, 66, 67 },
            [53153] = new uint[] { 88, 187, 248, 66, 67, 68, 69, 205 }, [53154] = Array.Empty<uint>(),
            [53155] = Array.Empty<uint>(), [53156] = new uint[] { 191, 238, 43, 66, 67 },
            [53157] = new uint[] { 11, 191, 238, 43, 66 }, [53158] = new uint[] { 1307 },
            [53159] = new uint[] { 88, 11, 170 }, [53160] = Array.Empty<uint>(), [53161] = new uint[] { 1307 },
            [53162] = new uint[] { 152, 39 }, [53163] = new uint[] { 39, 174, 1 }, [53164] = Array.Empty<uint>(),
            [53165] = new uint[] { 94, 95, 96, 97, 98, 99 }, [53166] = new uint[] { 88, 192 },
            [53167] = new uint[] { 39, 37, 192 }, [53168] = new uint[] { 187, 6, 5, 28, 29, 30 },
            [53169] = new uint[] { 722, 1305, 1306, 755, 754, 723 }, [53170] = new uint[] { 224, 222, 226, 71, 221 },
            [53171] = new uint[] { 224, 66, 71, 221, 88 }, [53172] = new uint[] { 67, 66, 39 },
            [53173] = new uint[] { 67, 66, 187, 39 }, [53174] = new uint[] { 67, 66, 187, 39 },
            [53175] = new uint[] { 39, 174, 192 }, [53176] = new uint[] { 1, 187 }, [53177] = new uint[] { 0, 39 },
            [53178] = new uint[] { 187, 3 }, [53179] = Array.Empty<uint>(),
            [53180] = new uint[] { 28, 29, 30, 134, 135, 136, 87, 73, 89 }, [53181] = new uint[] { 187 },
            [53182] = new uint[] { 39, 77, 84, 174 }, [53183] = new uint[] { 88, 67, 188 },
            [53184] = new uint[] { 0, 1305 }, [53185] = new uint[] { 0, 2, 37, 71, 39 }, [53187] = new uint[] { 39 },
            [53188] = new uint[] { 209 }, [53189] = new uint[] { 66, 24, 67, 39 },
            [53190] = new uint[] { 88, 66, 67, 68, 69 }, [53191] = new uint[] { 110, 240, 111 },
            [53192] = new uint[] { 77, 84, 72, 73, 28, 29, 30, 103, 151 }, [53193] = new uint[] { 44, 7 },
            [53194] = new uint[] { 123 }, [53195] = new uint[] { 66, 174, 67, 175, 68, 176, 69, 177 },
            [53196] = new uint[] { 0, 2, 37, 71 }, [53197] = new uint[] { 39 }, [53198] = new uint[] { 39 },
            [53199] = Array.Empty<uint>(), [53200] = new uint[] { 66 }, [53201] = new uint[] { 39 },
            [53202] = new uint[] { 187, 66, 67 }, [53203] = Array.Empty<uint>(), [53204] = new uint[] { 174 },
            [53205] = new uint[] { 174 }, [53206] = new uint[] { 174 }, [53207] = new uint[] { 39 },
            [53208] = new uint[] { 66, 67 }, [53209] = new uint[] { 187, 66, 67 }, [53210] = new uint[] { 39, 175 },
            [53211] = new uint[] { 88, 236, 187, 117, 237, 73 }, [53212] = new uint[] { 88, 236, 187, 117, 237 },
            [53213] = new uint[] { 88, 236, 117, 73, 237, 66 }, [53214] = new uint[] { 224 },
            [53215] = new uint[] { 0, 39 }, [53216] = Array.Empty<uint>(), [53217] = new uint[] { 191, 238 },
            [53218] = Array.Empty<uint>(), [53219] = new uint[] { 0, 168, 170, 172, 169, 171, 173, 71 },
            [53220] = new uint[] { 0, 174, 172, 175, 173 }, [53221] = new uint[] { 0, 174, 172, 175, 173 },
            [53222] = new uint[] { 77, 176, 172, 177, 173, 72, 73, 28, 29, 30, 103, 128, 129, 130, 131, 132, 133 },
            [53223] = new uint[] { 0, 39 }, [53224] = new uint[] { 88 },
            [53225] = new uint[] { 71, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235 },
            [53226] = new uint[] { 88, 39 }, [53227] = new uint[] { 188 },
            [53228] = new uint[] { 193, 194, 195, 196, 197, 198, 199, 200, 201 }, [53229] = new uint[] { 39 },
            [53230] = new uint[] { 39 }, [53231] = new uint[] { 39 }, [53232] = new uint[] { 39 },
            [53233] = new uint[] { 209 }, [53234] = new uint[] { 0, 39, 111, 71 },
            [53235] = new uint[] { 202, 203, 204, 205, 209 },
            [53236] = new uint[] { 209, 189, 39, 207, 43, 206, 187 }, [53237] = new uint[] { 209 },
            [53238] = new uint[] { 209, 86, 138, 28, 29, 30 }, [53239] = new uint[] { 209, 86 },
            [53240] = new uint[] { 194 }, [53241] = new uint[] { 209, 39, 5, 6 },
            [53242] = new uint[] { 172, 173, 168, 170, 169, 171 },
            [53243] = new uint[] { 209, 193, 86, 138, 28, 29, 30 }, [53244] = new uint[] { 76, 205, 201 },
            [53245] = Array.Empty<uint>(), [53246] = new uint[] { 187, 39, 66, 192 },
            [53247] = new uint[] { 11, 191, 238 }, [53249] = new uint[] { 11, 238 },
            [53250] = new uint[] { 187, 84, 66, 67, 68, 69 }, [53251] = new uint[] { 39, 175 },
            [53252] = new uint[] { 66 }, [53253] = new uint[] { 187, 248, 245, 243 },
            [53254] = new uint[] { 39, 175 }, [53255] = new uint[] { 212, 213, 11, 33 },
            [53256] = new uint[] { 39, 71 }, [53257] = new uint[] { 244 }, [53259] = new uint[] { 88, 212, 213, 11 },
            [53260] = new uint[] { 39, 209 }, [53261] = new uint[] { 39, 209 }, [53262] = new uint[] { 88 },
            [53263] = new uint[] { 88 }, [53264] = new uint[] { 88 }, [53265] = new uint[] { 39 },
            [53266] = new uint[] { 193, 194, 195, 196, 197, 198, 199, 200, 201 }, [53267] = Array.Empty<uint>(),
            [53268] = new uint[] { 209 }, [53271] = Array.Empty<uint>(), [53272] = Array.Empty<uint>(),
            [53273] = new uint[] { 212, 213, 11 }, [53274] = Array.Empty<uint>(), [53275] = new uint[] { 39 },
            [53277] = Array.Empty<uint>(), [53278] = new uint[] { 251, 252, 253 }, [53286] = new uint[] { 39, 11 },
        };

        public record Stat(uint? Id, object Value);
        public record GameAction(uint Action, uint Unknown, uint Version, List<int[]> Criteria, List<Stat> Stats);
        public record CriteriaEntry(uint Key, List<int[]> Criteria);

        public class TemplateList
        {
            public int Type { get; set; }
            public int Entry { get; set; }
            public List<uint[]> Stats { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public List<GameAction> Actions { get; set; }
            public List<CriteriaEntry> Criteria { get; set; }
        }

        public class Template
        {
            public uint Kind { get; set; }
            public List<TemplateList> Lists { get; set; } = new();
            public List<int> Delta { get; set; }
        }

        public record Encrypted(string Format, uint Version, uint Flags, uint Size, uint Unused, int Payload);

        public static object Parse(byte[] data)
        {
            if (data.AsSpan().StartsWith("RDBH"u8))
            {
                // RDBH: u32 version (1), u32 flags (& 0xf = 1: TEA), u32 real size, u32 unused, then 8-byte TEA blocks
                var r = new SpanReader(data) { Position = 4 };
                return new Encrypted("RDBH (TEA-encrypted with a key the server sends)", r.U32(), r.U32(), r.U32(), r.U32(), data.Length - 20);
            }

            var plan = new List<int>();
            while (true)
            {
                var reader = new Reader(data, plan);
                Exception error;
                try
                {
                    var template = ParseTemplate(reader);
                    if (reader.Position == data.Length)
                    {
                        if (reader.Deltas.Any(d => d != 0)) template.Delta = reader.Deltas;
                        return template;
                    }
                    error = new InvalidDataException($"Consumed {reader.Position} of {data.Length} bytes.");
                }
                catch (InvalidDataException e)
                {
                    error = e;
                }
                var choices = reader.Choices;
                while (choices.Count > 0 && choices[^1] + 1 > MaxValues) choices.RemoveAt(choices.Count - 1);
                if (choices.Count == 0) throw error;
                plan = choices.Take(choices.Count - 1).Append(choices[^1] + 1).ToList();
            }
        }

        private static Template ParseTemplate(Reader r)
        {
            var template = new Template { Kind = r.U32() };
            if (template.Kind < 10000) throw new InvalidDataException($"Bogus kind {template.Kind}.");
            var count = r.U32();
            if (count > 1000) throw new InvalidDataException($"Bogus list count {count}.");
            for (; count > 0; count--)
            {
                var list = new TemplateList { Type = r.I32(), Entry = r.I32() };
                if (list.Type < 0 || list.Type > 49 || list.Entry < 0 || list.Entry > 50) throw new InvalidDataException($"Bogus list pair {list.Type}, {list.Entry}.");
                switch (list.Type)
                {
                    case 15:
                        if (list.Entry == 43) throw new InvalidDataException("ItemServerDynamicList is no longer supported.");
                        if (list.Entry == 23)
                        {
                            list.Stats = new List<uint[]>();
                            for (var n = r.Count("stat list"); n > 0; n--) list.Stats.Add(new[] { r.U32(), r.U32() });
                        }
                        break;
                    case 21:
                        var nameLength = r.U16();
                        var descriptionLength = r.U16();
                        list.Name = r.Utf8(nameLength);
                        list.Description = r.Utf8(descriptionLength);
                        break;
                    case 2:
                        list.Actions = new List<GameAction>();
                        for (var n = r.Count("GameActionList", 1000); n > 0; n--) list.Actions.Add(ReadAction(r));
                        break;
                    case 22:
                        list.Criteria = new List<CriteriaEntry>();
                        for (var n = r.Count("CriteriaMap", 1000); n > 0; n--)
                        {
                            var key = r.U32();
                            list.Criteria.Add(new CriteriaEntry(key, ReadCriteria(r, r.Count("criteria", 30001))));
                        }
                        break;
                    case 6:
                        if (list.Entry is 12 or 13 or 48) throw new InvalidDataException($"Unsupported list {list.Type}, {list.Entry}.");
                        break;
                    default:
                        throw new InvalidDataException($"List type {list.Type} isn't handled.");
                }
                template.Lists.Add(list);
            }
            return template;
        }

        private static List<int[]> ReadCriteria(Reader r, int count)
        {
            var criteria = new List<int[]>();
            for (var i = 0; i < count; i++)
            {
                var c = new[] { r.I32(), r.I32(), r.I32() };
                if (c[2] < 0 || c[2] >= 0x402) throw new InvalidDataException($"Criterion operator {c[2]}.");
                criteria.Add(c);
            }
            return criteria;
        }

        private static GameAction ReadAction(Reader r)
        {
            var action = r.U32();
            var unknown = r.U32();
            var version = r.U32();
            if (version != 4) throw new InvalidDataException($"GameAction version {version}, the client wants 4.");
            if (action < 0xcf09 || action > 0xd033) throw new InvalidDataException($"GameAction type {action} out of range.");
            var criteriaCount = r.I32();
            if (criteriaCount > 1000) throw new InvalidDataException("More than 1000 criteria.");
            var criteria = ReadCriteria(r, criteriaCount);
            var stats = FixedStats.Select(s => new Stat(s, r.U32())).ToList();

            //An action type the build doesn't register has no slots: the client logs it and reads none
            var slots = Schema.GetValueOrDefault(action, Array.Empty<uint>());
            var options = Counts(slots.Length);
            var k = r.Choices.Count;
            var choice = k < r.Plan.Count ? r.Plan[k] : 0;
            if (choice >= options.Count) throw new InvalidDataException("No more value counts to try.");
            var valueCount = options[choice];
            r.Choices.Add(choice);
            r.Deltas.Add(valueCount - slots.Length);
            for (var i = 0; i < valueCount; i++)
            {
                if (i < slots.Length)
                {
                    var stat = ItemTemplateParser.V5Stats.GetValueOrDefault(slots[i] & 0xff);
                    stats.Add(new Stat(stat, StringStats.Contains(stat) ? r.String() : r.U32()));
                }
                else
                {
                    stats.Add(new Stat(null, r.LooksLikeString() ? r.String() : r.U32()));
                }
            }
            return new GameAction(action, unknown, version, criteria, stats);
        }

        /// <summary>Value counts to try for an action whose schema has n slots: n first, then n ± 1, n ± 2, …</summary>
        private static List<int> Counts(int n)
        {
            var counts = new List<int> { n };
            for (var d = 1; d <= MaxValues; d++)
            {
                foreach (var c in new[] { n + d, n - d })
                {
                    if (c >= 0 && c <= MaxValues) counts.Add(c);
                }
            }
            return counts;
        }

        private class Reader : SpanReader
        {
            private readonly byte[] _data;

            public List<int> Plan { get; }
            public List<int> Choices { get; } = new();
            public List<int> Deltas { get; } = new();

            public Reader(byte[] data, List<int> plan) : base(data)
            {
                _data = data;
                Plan = plan;
            }

            public int Count(string what, int? limit = null)
            {
                var v = I32();
                if (v <= 0 || v % 1009 != 0) throw new InvalidDataException($"Inconsistent {what} size {v}.");
                var n = v / 1009 - 1;
                if (n >= limit) throw new InvalidDataException($"{what}: {n} entries.");
                return n;
            }

            public string Utf8(int count) => Encoding.UTF8.GetString(Bytes(count));

            /// <summary>BinaryStream::ReadString: u16 n &lt; 0x8000, or ffff, u16 0x1ff, u32 n &gt;= 0x8000; then n bytes.</summary>
            public string String()
            {
                int n = U16();
                if (n >= 0x8000)
                {
                    var m = U16();
                    var n2 = U32();
                    if (!(n == 0xffff && m == 0x1ff && n2 >= 0x8000)) throw new InvalidDataException("Bad long string header.");
                    if (n2 > int.MaxValue) throw new InvalidDataException("String past the end.");
                    n = (int)n2;
                }
                return Utf8(n);
            }

            public bool LooksLikeString()
            {
                if (Remaining < 2) return false;
                var n = BitConverter.ToUInt16(_data, Position);
                if (n == 0 || Remaining < 2 + n) return false;
                for (var i = 0; i < n; i++)
                {
                    var c = _data[Position + 2 + i];
                    if (c < 32 || c >= 127) return false;
                }
                return true;
            }
        }
    }
}
