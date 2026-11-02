using RdbExporter.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Item (1000620) and spell (1000621) templates, as GameItem::LoadStaticData reads them:
    ///   u32 version (5), u32 kind
    ///   u32 n, n × {u32 slot, GameActionList: u32 version (1), u32 n, n × GameAction}
    ///     GameAction: u32 action, u32 unknown, u32 version (6, or 5 with stats remapped through a table), criteria, stats
    ///   u32 n, n × {u32 key, u32 version (1), criteria}
    ///   u32 stat list version (1), stats, string name, string description
    /// stats = u32 n, n × {u32 stat, u8 type, value (0: u32, 1: f32, 2: string with a u32 length)}; in a GameAction a
    /// type 0 stat 1241 / 1242 is a string with a u16 length. criteria = u32 n, n × 3 u32. string = u16 n, n bytes UTF-8.
    ///
    /// The tool that wrote some entries stored the string's UTF-16 length instead of its byte count, followed either by
    /// the UTF-8 bytes ("chars") or by a .NET BinaryWriter string ("dotnet", also where a u32 length belongs). Each string
    /// picks from the readings that fit, trying the client's ("bytes") first, and the parser backtracks over them until
    /// the whole entry is consumed.
    /// </summary>
    public static class ItemTemplateParser
    {
        private static readonly int[] StringStats = { 1241, 1242 };

        //GameAction version 5 stats are a byte index into this table: runs of (first index, first stat, count).
        private static readonly (int Index, int Stat, int Count)[] V5StatRuns =
        {
            (0, 722, 46), (46, 769, 31), (77, 1110, 1), (78, 1114, 3), (81, 1295, 1), (82, 1117, 19), (101, 1137, 1),
            (103, 1138, 12), (116, 1150, 4), (120, 1163, 5), (125, 1159, 2), (127, 1162, 1), (128, 1168, 13),
            (150, 1181, 19), (169, 1201, 86),
        };
        private static readonly Dictionary<uint, uint> V5Stats = V5StatRuns
            .SelectMany(r => Enumerable.Range(0, r.Count).Select(k => (Index: (uint)(r.Index + k), Stat: (uint)(r.Stat + k))))
            .ToDictionary(x => x.Index, x => x.Stat);

        public static ItemTemplate Parse(byte[] data)
        {
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
                        if (reader.Choices.Any(c => c.Kind != "bytes")) template.StringReadings = reader.Choices.Select(c => c.Kind).ToList();
                        return template;
                    }
                    error = new InvalidDataException($"Consumed {reader.Position} of {data.Length} bytes.");
                }
                catch (InvalidDataException e)
                {
                    error = e;
                }

                var choices = reader.Choices;
                while (choices.Count > 0 && choices[^1].Choice + 1 >= choices[^1].Count) choices.RemoveAt(choices.Count - 1);
                if (choices.Count == 0) throw error;
                plan = choices.Take(choices.Count - 1).Select(c => c.Choice).Append(choices[^1].Choice + 1).ToList();
            }
        }

        private static ItemTemplate ParseTemplate(Reader reader)
        {
            var template = new ItemTemplate { Version = reader.U32() };
            if (template.Version != 5) throw new InvalidDataException($"Version {template.Version}, the client wants 5.");
            template.Kind = reader.U32();

            for (var count = reader.U32(); count > 0; count--)
            {
                var list = new ItemActionList { Slot = (int)reader.U32() };
                var listVersion = reader.U32();
                if (listVersion != 1) throw new InvalidDataException($"GameActionList version {listVersion}.");
                for (var actions = reader.U32(); actions > 0; actions--)
                {
                    var action = new ItemGameAction { Action = reader.U32(), Unknown = reader.U32(), Version = reader.U32() };
                    if (action.Version != 5 && action.Version != 6) throw new InvalidDataException($"GameAction version {action.Version}.");
                    action.Criteria = ReadCriteria(reader);
                    if (action.Criteria.Count > 1000) throw new InvalidDataException("More than 1000 criteria.");
                    action.Stats = ReadStats(reader, true, action.Version == 5);
                    list.Actions.Add(action);
                }
                template.ActionLists.Add(list);
            }

            for (var count = reader.U32(); count > 0; count--)
            {
                var item = new ItemCriteriaItem { Key = reader.U32() };
                var itemVersion = reader.U32();
                if (itemVersion != 1) throw new InvalidDataException($"CriteriaItem version {itemVersion}.");
                item.Criteria = ReadCriteria(reader);
                template.CriteriaItems.Add(item);
            }

            var statsVersion = reader.U32();
            if (statsVersion != 1) throw new InvalidDataException($"Stat list version {statsVersion}.");
            template.Stats = ReadStats(reader, false, false);
            template.Name = reader.String16();
            template.Description = reader.String16();
            return template;
        }

        private static List<uint[]> ReadCriteria(Reader reader)
        {
            var criteria = new List<uint[]>();
            for (var count = reader.U32(); count > 0; count--)
            {
                criteria.Add(new[] { reader.U32(), reader.U32(), reader.U32() });
            }
            return criteria;
        }

        private static List<ItemStat> ReadStats(Reader reader, bool gameAction, bool version5)
        {
            var stats = new List<ItemStat>();
            for (var count = reader.U32(); count > 0; count--)
            {
                var stat = reader.U32();
                var type = reader.U8();
                if (version5) stat = V5Stats.GetValueOrDefault(stat & 0xff);
                stats.Add(type switch
                {
                    0 when gameAction && StringStats.Contains((int)stat) => new ItemStat(stat, "string", reader.String16()),
                    0 => new ItemStat(stat, "u32", reader.U32()),
                    1 => new ItemStat(stat, "f32", reader.F32()),
                    2 => new ItemStat(stat, "string", reader.String32()),
                    _ => throw new InvalidDataException($"Unsupported stat type {type}."),
                });
            }
            return stats;
        }

        private class Reader
        {
            private readonly byte[] _data;
            private readonly List<int> _plan;

            public int Position { get; private set; }
            public List<(int Choice, int Count, string Kind)> Choices { get; } = new();

            public Reader(byte[] data, List<int> plan)
            {
                _data = data;
                _plan = plan;
            }

            private ReadOnlySpan<byte> Take(int count)
            {
                if (Position + count > _data.Length) throw new InvalidDataException("Read past the end.");
                var span = _data.AsSpan(Position, count);
                Position += count;
                return span;
            }

            public byte U8() => Take(1)[0];
            public uint U32() => BitConverter.ToUInt32(Take(4));
            public float F32() => BitConverter.ToSingle(Take(4));
            public string String16() => ReadString(BitConverter.ToUInt16(Take(2)), false);
            public string String32() => ReadString(U32(), true);

            private string ReadString(long length, bool wide)
            {
                var at = Position;
                var options = new List<(string Kind, long End, int Start)>();
                if (!(wide && length > _data.Length)) options.Add(("bytes", at + length, at));
                var charsEnd = Utf16End(at, length);
                if (charsEnd != null && charsEnd != at + length) options.Add(("chars", charsEnd.Value, at));
                if (wide)
                {
                    //"dotnet" wrote a u16 even where the client reads a u32
                    at -= 2;
                    length &= 0xffff;
                }
                var (byteCount, textStart) = Varint(at);
                if (byteCount != null && Utf16End(textStart, length) == textStart + byteCount) options.Add(("dotnet", textStart + byteCount.Value, textStart));
                if (options.Count == 0) throw new InvalidDataException($"No string reading fits at {at}.");

                var k = Choices.Count;
                var choice = k < _plan.Count ? _plan[k] : 0;
                Choices.Add((choice, options.Count, options[choice].Kind));
                var (_, end, start) = options[choice];
                if (end > _data.Length) throw new InvalidDataException("String past the end.");
                Position = (int)end;
                return Encoding.UTF8.GetString(_data, start, (int)end - start);
            }

            /// <summary>End of the UTF-8 text at <paramref name="position"/> that is <paramref name="units"/> UTF-16 code units long.</summary>
            private long? Utf16End(long position, long units)
            {
                long count = 0;
                while (count < units)
                {
                    if (position >= _data.Length) return null;
                    var c = _data[position];
                    var length = c < 0x80 ? 1 : c < 0xe0 ? 2 : c < 0xf0 ? 3 : 4;
                    count += length == 4 ? 2 : 1;
                    position += length;
                }
                return count == units ? position : null;
            }

            private (long? Value, int End) Varint(int position)
            {
                long value = 0;
                for (var shift = 0; ; shift += 7)
                {
                    if (position >= _data.Length || shift > 56) return (null, position);
                    var c = _data[position++];
                    value |= (long)(c & 0x7f) << shift;
                    if (c < 0x80) return (value, position);
                }
            }
        }
    }
}
