using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;

namespace RdbExporter.Utilities
{
    /// <summary>
    /// Little-endian reader over an entry's bytes that throws InvalidDataException when a read runs past the end.
    /// </summary>
    public class SpanReader
    {
        private readonly byte[] _data;

        public SpanReader(byte[] data) => _data = data;

        public int Position { get; set; }
        public int Length => _data.Length;
        public int Remaining => _data.Length - Position;

        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || count > Remaining) throw new InvalidDataException($"Needs {count} bytes at 0x{Position:x}, {Remaining} left.");
            var span = _data.AsSpan(Position, count);
            Position += count;
            return span;
        }

        public byte U8() => Bytes(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Bytes(4));
        public float[] F32s(int count) => Enumerable.Range(0, count).Select(_ => F32()).ToArray();
        public uint[] U32s(int count) => Enumerable.Range(0, count).Select(_ => U32()).ToArray();
        public string Latin1(int count) => Encoding.Latin1.GetString(Bytes(count));

        public void ExpectEnd()
        {
            if (Remaining != 0) throw new InvalidDataException($"Consumed {Position} of {Length} bytes.");
        }
    }
}
