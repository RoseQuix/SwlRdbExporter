using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Static mesh placements (statels, 1000060, RDBStatels_c), one entry per playfield:
    ///   u32 version (0x5000-0x5004), u32 count, then count records:
    ///   0x5000: u32 id, 16 f32 row-major matrix (translation in row 3). Some 0x5000 entries instead hold count groups
    ///           of {u32 k, k × {u32 id, u32 id2, 16 f32}}.
    ///   later:  [&gt;= 0x5002: u32 visual type, else 1010030] u32 id, f32 position[3], f32 rotation[4] (x, y, z, w),
    ///           f32 scale, u32 flags; flags &amp; 1: 12 f32; flags &amp; 2: 5 f32 [&gt;= 0x5002: 2 f32, u32 (== 1)];
    ///           flags &amp; 4: u32; flags &amp; 8: no data; &gt;= 0x5003: u32 instance id; &gt;= 0x5004: u32 n, n × {u32, u32, 4 f32}.
    /// The visual is usually a mesh index file (1010030 .idx) or a monster data file (1010201).
    /// </summary>
    public static class StatelsParser
    {
        private const int IdxType = 1010030;

        public class Statels
        {
            public uint Version { get; set; }
            public string Variant { get; set; }
            public List<Record> Records { get; set; } = new();
        }

        public class Record
        {
            public uint Type { get; set; }
            public uint Id { get; set; }
            public string Name { get; set; }
            public uint? Id2 { get; set; }
            public float[] Position { get; set; }
            public float[] Rotation { get; set; }
            public float Scale { get; set; }
            public uint Flags { get; set; }
            public float[] Matrix { get; set; }
            public float[] Matrix3x4 { get; set; }
            public float[] Values5 { get; set; }
            public float[] Values2 { get; set; }
            public bool? Values2Flag { get; set; }
            public uint? Value4 { get; set; }
            public uint? Instance { get; set; }
            public List<object> Extra { get; set; }
        }

        public static Statels Parse(byte[] data)
        {
            var r = new Reader(data);
            var result = new Statels { Version = r.U32() };
            var count = r.U32();
            if (result.Version == 0x5000)
            {
                result.Variant = 8 + 68L * count == data.Length ? "plain" : "grouped";
                for (var i = 0; i < count; i++)
                {
                    var groupSize = result.Variant == "grouped" ? r.U32() : 1;
                    for (var k = 0; k < groupSize; k++)
                    {
                        var record = new Record { Type = IdxType, Id = r.U32() };
                        if (result.Variant == "grouped") record.Id2 = r.U32();
                        record.Matrix = r.F32s(16);
                        (record.Position, record.Rotation, record.Scale) = MatrixToTrs(record.Matrix);
                        result.Records.Add(record);
                    }
                }
            }
            else if (result.Version >= 0x5001 && result.Version <= 0x5004)
            {
                for (var i = 0; i < count; i++)
                {
                    var record = new Record { Type = result.Version > 0x5001 ? r.U32() : IdxType, Id = r.U32(), Position = r.F32s(3), Rotation = r.F32s(4), Scale = r.F32() };
                    var flags = record.Flags = r.U32();
                    if ((flags & 1) != 0) record.Matrix3x4 = r.F32s(12);
                    if ((flags & 2) != 0)
                    {
                        record.Values5 = r.F32s(5);
                        if (result.Version > 0x5001)
                        {
                            record.Values2 = r.F32s(2);
                            record.Values2Flag = r.U32() == 1;
                        }
                    }
                    if ((flags & 4) != 0) record.Value4 = r.U32();
                    if (result.Version > 0x5002) record.Instance = r.U32();
                    if (result.Version > 0x5003)
                    {
                        var extraCount = r.U32();
                        record.Extra = Enumerable.Range(0, (int)extraCount).Select(_ => (object)new { A = r.U32(), B = r.U32(), Values = r.F32s(4) }).ToList();
                    }
                    result.Records.Add(record);
                }
            }
            else
            {
                throw new InvalidDataException($"Unsupported statels version 0x{result.Version:x}.");
            }
            if (r.Remaining != 0) throw new InvalidDataException($"{r.Remaining} bytes left after the records.");
            return result;
        }

        /// <summary>
        /// Position, rotation quaternion (x, y, z, w) and uniform scale of a row-major matrix, as the client takes them:
        /// scale = |row 0|, rows normalised before converting.
        /// </summary>
        private static (float[], float[], float) MatrixToTrs(float[] m)
        {
            double M(int row, int column) => m[row * 4 + column];
            var scale = Math.Sqrt(M(0, 0) * M(0, 0) + M(0, 1) * M(0, 1) + M(0, 2) * M(0, 2));
            //Column-vector rotation matrix R = transpose(upper 3x3) / scale
            double R(int i, int j) => M(j, i) / scale;

            var trace = R(0, 0) + R(1, 1) + R(2, 2);
            var largest = R(0, 0) >= R(1, 1) && R(0, 0) >= R(2, 2) ? 0 : R(1, 1) >= R(2, 2) ? 1 : 2;
            var q = new double[4];
            if (trace >= R(largest, largest))
            {
                var w = Math.Sqrt(1 + trace) / 2;
                q[0] = (R(2, 1) - R(1, 2)) / (4 * w);
                q[1] = (R(0, 2) - R(2, 0)) / (4 * w);
                q[2] = (R(1, 0) - R(0, 1)) / (4 * w);
                q[3] = w;
            }
            else
            {
                int i = largest, j = (i + 1) % 3, k = (i + 2) % 3;
                var a = Math.Sqrt(1 + R(i, i) - R(j, j) - R(k, k)) / 2;
                q[i] = a;
                q[j] = (R(j, i) + R(i, j)) / (4 * a);
                q[k] = (R(k, i) + R(i, k)) / (4 * a);
                q[3] = (R(k, j) - R(j, k)) / (4 * a);
            }
            return (new[] { m[12], m[13], m[14] }, q.Select(x => (float)x).ToArray(), (float)scale);
        }

        private class Reader
        {
            private readonly byte[] _data;
            private int _position;

            public Reader(byte[] data) => _data = data;

            public int Remaining => _data.Length - _position;

            public uint U32()
            {
                if (Remaining < 4) throw new InvalidDataException("Read past the end.");
                var value = BitConverter.ToUInt32(_data, _position);
                _position += 4;
                return value;
            }

            public float F32() => BitConverter.UInt32BitsToSingle(U32());
            public float[] F32s(int count) => Enumerable.Range(0, count).Select(_ => F32()).ToArray();
        }
    }
}
