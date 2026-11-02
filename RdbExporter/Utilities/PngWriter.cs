using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RdbExporter.Utilities
{
    /// <summary>
    /// Minimal PNG encoder for greyscale images, which SkiaSharp can't write at 16 bits.
    /// </summary>
    public static class PngWriter
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>Writes 16-bit greyscale samples, row by row from the top.</summary>
        public static void WriteGray16(Stream output, int width, int height, ReadOnlySpan<ushort> samples)
        {
            var raw = new byte[height * (1 + 2 * width)];
            for (var y = 0; y < height; y++)
            {
                var row = raw.AsSpan(y * (1 + 2 * width) + 1); //Filter type 0
                for (var x = 0; x < width; x++)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(row[(2 * x)..], samples[y * width + x]);
                }
            }
            Write(output, width, height, 16, raw);
        }

        /// <summary>Writes 8-bit greyscale samples, row by row from the top.</summary>
        public static void WriteGray8(Stream output, int width, int height, ReadOnlySpan<byte> samples)
        {
            var raw = new byte[height * (1 + width)];
            for (var y = 0; y < height; y++)
            {
                samples.Slice(y * width, width).CopyTo(raw.AsSpan(y * (1 + width) + 1));
            }
            Write(output, width, height, 8, raw);
        }

        private static void Write(Stream output, int width, int height, byte bitDepth, byte[] filteredRows)
        {
            output.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a });

            var header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            header[8] = bitDepth;
            header[9] = 0; //Greyscale
            WriteChunk(output, "IHDR", header);

            using (var compressed = new MemoryStream())
            {
                using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                {
                    zlib.Write(filteredRows);
                }
                WriteChunk(output, "IDAT", compressed.ToArray());
            }
            WriteChunk(output, "IEND", Array.Empty<byte>());
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
            output.Write(buffer);
            var typeBytes = Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes);
            output.Write(data);
            BinaryPrimitives.WriteUInt32BigEndian(buffer, ~Crc(Crc(0xffffffff, typeBytes), data));
            output.Write(buffer);
        }

        private static uint Crc(uint crc, byte[] data)
        {
            foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
            return crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
