using RdbExporter.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// The small one-entry data tables: feats, hair, playfield parents and character-creator assets (1000042-1000099)
    /// and the game-data tables (1000505-1000524, 1000625 / 1000626, 1065101-1065211). Each entry is one table after the
    /// inner header, read by its own data-table class in the client. str32 / str16 are strings with a u32 / u16 length.
    /// Columns the notes couldn't name are given as plain value arrays.
    /// </summary>
    public static class DataTableParser
    {
        private static readonly Dictionary<int, Func<Reader, DataTable>> Tables = new()
        {
            //Feats (FeatTable_t, FeatClassEntry_c), hair (RDBHairReplacement), playfields (RDBPlayfieldName_c)
            [1000042] = Feats,
            [1000043] = r => { var n = r.U32(); var v = r.Version(2, 3); return Table(v, r.Rows(n, () => r.U32s(10))); },
            [1000099] = r => { var v = r.Version(1); return Table(v, r.Rows(r.U32(), () => new { Feat = r.U32(), Kind = r.U32(), Cost = r.U32() })); },
            [1000044] = r => Table(null, r.Rows(r.U32(), () => new { Key = r.U32(), Points = r.U32(), Value = r.U32() })),
            [1000080] = r => Table(null, r.Rows(r.U32(), () => new { HairType = r.U32(), Mesh = r.U32() })),
            [1000081] = r => Table(null, r.Rows(r.U32(), () => new { HairType = r.U32(), From = r.U32(), To = r.U32() })),
            [1000052] = r => Table(null, r.Rows(r.U32(), () => new { Playfield = r.U32(), Parent = r.U32() })),
            [1000071] = r => { var n = r.U32(); var v = r.Version(1); return Table(v, r.Rows(n, () => new { Category = (int)r.U32(), Id = r.U32() })); },

            //Game data
            [1000505] = r => Table(null, r.Rows(r.U32(), () => new { Class = r.U32(), Name = r.Str32(), Value = r.U32() })),
            [1000506] = r => Table(null, r.Rows(r.U32(), () => new { Value = r.U32(), Class = r.U32() })),
            [1000507] = r => Table(null, r.Rows(r.U32(), () => new { Value = r.U32(), Class = r.U32() })),
            [1000508] = r => Table(null, r.Rows(r.U32(), () => new { Id = r.U32(), Name = r.Str32() })),
            [1000509] = r => { var n = r.U32(); if (r.Remaining == 8 * n) return Table(null, r.Rows(n, () => r.U32s(2))); var v = r.Version(1); return Table(v, r.Rows(n, () => r.U32s(3))); },
            [1000510] = r => Table(null, r.Rows(r.U32(), () => r.U32s(21))),
            [1000511] = r => { var v = r.Version(2); return Table(v, r.Rows(r.U32(), () => r.U32s(14))); },
            [1000512] = r => Table(null, r.Rows(r.U32(), () => r.U32s(2))),
            [1000513] = r => Table(null, r.Rows(r.U32(), () => r.U32s(6))),
            [1000514] = r => Table(null, r.Rows(r.U32(), () => r.U32s(6))),
            [1000515] = r => { var n = r.U32(); var v = r.Version(3); return Table(v, r.Rows(n, () => r.U32s(6))); },
            [1000516] = r => Table(null, r.Rows(r.U32(), () => new { Name = r.Str32(), Value = r.F64() })),
            [1000517] = r => { var n = r.U32(); var v = r.Version(1); return Table(v, r.Rows(n, () => new { Id = r.U32(), Name = r.Str32() })); },
            [1000518] = r => { var n = r.U32(); var v = r.Version(1); return Table(v, r.Rows(n, () => new { Values = r.U32s(4), Name = r.Str32() })); },
            [1000520] = Tokens,
            [1000521] = r => { var n = r.U32(); var v = r.Version(1); return Table(v, r.Rows(n, () => r.U32s(3))); },
            [1000522] = r => { var n = r.U32(); var v = r.Version(1); return Table(v, r.Rows(n, () => r.U32s(3))); },
            [1000523] = r => { var n = r.U32(); var v = r.Version(1); return Table(v, r.Rows(n, () => new { Playfield = r.U32(), Values = r.U32s(2), Factor = r.F32() })); },
            [1000524] = r => { var n = r.U32(); var v = r.Version(1); return Table(v, r.Rows(n, () => new { Values = r.U32s(5), Abilities = r.U32s(14) })); },
            [1000625] = r => { var v = r.Version(1); return Table(v, r.Rows(r.U32(), () => new { Value = r.U32(), Spell = r.U32(), Flag = r.U32(), Value2 = r.U32(), Events = Enumerable.Range(0, 7).Select(_ => r.Str16()).ToArray() })); },
            [1000626] = r => { var v = r.Version(1); return Table(v, r.Rows(r.U32(), () => new { Language = r.Str32(), Words = r.Rows(r.U32(), r.Str32) })); },
            [1065101] = r => Table(null, r.Rows(r.U32(), () => r.U32s(4))),
            [1065103] = r => Table(null, r.Rows(r.U32(), () => new { Level = r.U32(), Rating = r.U32() })),
            [1065201] = LevelXp,
            [1065202] = LevelXp,
            [1065203] = LevelXp,
            [1065204] = r => Table(null, r.Rows(r.U32(), () => new { Table = r.U32(), Level = r.U32(), Xp = r.U32() })),
            [1065205] = r => Table(null, r.Rows(r.U32(), () => new { Item = r.U32(), Values = r.U32s(2) })),
            [1065206] = r => Table(null, r.Rows(r.U32(), () => new { Level = r.U32(), Ap = r.U32(), Sp = r.U32() })),
            [1065207] = r => Table(null, r.Rows(r.U32(), () => r.U32s(3))),
            [1065208] = r => Table(null, r.Rows(r.U32(), () => new { Kind = r.U32(), Level = r.U32(), Values = r.U32s(3) })),
            [1065209] = r => Table(null, r.Rows(r.U32(), () => new { Id = r.U32(), Start = r.UnixTime(), End = r.UnixTime(), Yearly = r.U32() })),
            [1065210] = r => Table(null, r.Rows(r.U32(), () => new { Period = r.U32(), Day = r.U32(), Item = r.U32() })),
            [1065211] = LevelXp,
        };

        public static bool IsKnownType(int type) => Tables.ContainsKey(type);

        public static DataTable Parse(int type, byte[] data)
        {
            if (!Tables.TryGetValue(type, out var parse)) throw new InvalidDataException($"No table layout for type {type}.");
            var reader = new Reader(data);
            var table = parse(reader);
            if (reader.Remaining != 0) throw new InvalidDataException($"{reader.Remaining} bytes left after the table.");
            return table;
        }

        private static DataTable Table(uint? version, IEnumerable<object> rows) => new() { Version = version, Rows = rows.ToList() };

        private static DataTable LevelXp(Reader r) => Table(null, r.Rows(r.U32(), () => new { Level = r.U32(), Xp = r.U32() }));

        /// <summary>u32 count, u32 version (0-2); feat, 1000620 template, 3 prerequisite feats, 5 more, then the cluster.</summary>
        private static DataTable Feats(Reader r)
        {
            var count = r.U32();
            var version = r.Version(0, 1, 2);
            return Table(version, r.Rows(count, () =>
            {
                var feat = r.U32();
                var template = r.U32();
                var prerequisites = r.U32s(3);
                var values = r.U32s(5);
                uint cluster, value11;
                if (version == 1) { value11 = r.U32(); cluster = r.U32(); }
                else if (version == 2) { cluster = r.U32(); value11 = r.U32(); }
                else { cluster = feat / 100; value11 = 0; }
                return new { Feat = feat, Template = template, Prerequisites = prerequisites, Values = values, Value11 = value11, Cluster = cluster };
            }));
        }

        /// <summary>u8 version (3), u8 count; {u8 id, version &gt; 1: str16 name, u8, u32, u32, version &gt; 2: str16, str16}.</summary>
        private static DataTable Tokens(Reader r)
        {
            var version = r.U8();
            return Table(version, r.Rows(r.U8(), () => new
            {
                Id = r.U8(),
                Name = version > 1 ? r.Str16() : "",
                Value = r.U8(),
                Values = r.U32s(2),
                Strings = version > 2 ? new[] { r.Str16(), r.Str16() } : null,
            }));
        }

        private class Reader
        {
            private readonly byte[] _data;
            private int _position;

            public Reader(byte[] data) => _data = data;

            public int Remaining => _data.Length - _position;

            private ReadOnlySpan<byte> Take(int count)
            {
                if (count > Remaining) throw new InvalidDataException($"Needs {count} bytes at {_position}, {Remaining} left.");
                var span = _data.AsSpan(_position, count);
                _position += count;
                return span;
            }

            public byte U8() => Take(1)[0];
            public uint U32() => BitConverter.ToUInt32(Take(4));
            public float F32() => BitConverter.ToSingle(Take(4));
            public double F64() => BitConverter.ToDouble(Take(8));
            public uint[] U32s(int count) => Enumerable.Range(0, count).Select(_ => U32()).ToArray();
            public DateTime UnixTime() => DateTime.UnixEpoch.AddSeconds(BitConverter.ToUInt64(Take(8)));
            public string Str16() => ReadString(BitConverter.ToUInt16(Take(2)));
            public string Str32() => ReadString((int)U32());

            private string ReadString(int length)
            {
                var bytes = Take(length);
                try
                {
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    return Encoding.Latin1.GetString(bytes);
                }
            }

            public uint Version(params uint[] accepted)
            {
                var version = U32();
                if (!accepted.Contains(version)) throw new InvalidDataException($"Version {version}, expected {string.Join(" or ", accepted)}.");
                return version;
            }

            public List<object> Rows<T>(uint count, Func<T> readRow)
            {
                var rows = new List<object>();
                for (var i = 0; i < count; i++) rows.Add(readRow());
                return rows;
            }
        }
    }
}
