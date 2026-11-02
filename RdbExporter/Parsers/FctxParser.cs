using RdbExporter.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Funcom FCTX textures (1010004, 1066606, 1000096, 1010006, 1010218, 1066610).
    /// 24-byte header: "FCTX", u32 version (2), u32 flags, u16 width, u16 height, u8 mip count, 3 bytes padding, fourcc.
    /// There is one layer per set bit of flags &amp; 7. The first layer's fourcc is the header's, each later layer starts with
    /// its own fourcc. Within every layer the mips are stored smallest first, and block counts round up.
    /// MIXD is BC5 (ATI2) with a 6-byte block (a low-precision copy of the green channel, unused by the client) stored
    /// before each mip's BC5 blocks.
    /// </summary>
    public static class FctxParser
    {
        private static readonly Dictionary<string, (int BlockSize, int ExtraBlockSize)> Formats = new()
        {
            ["DXT1"] = (8, 0),
            ["DXT5"] = (16, 0),
            ["ATI1"] = (8, 0),
            ["ATI2"] = (16, 0),
            ["MIXD"] = (16, 6),
        };

        public static bool IsFctx(byte[] data) => data.Length >= 24 && Encoding.ASCII.GetString(data, 0, 4) == "FCTX";

        public static FctxFile Parse(byte[] data)
        {
            if (!IsFctx(data)) throw new InvalidDataException("Missing FCTX magic.");

            var flags = BitConverter.ToUInt32(data, 8);
            var file = new FctxFile
            {
                Width = BitConverter.ToUInt16(data, 12),
                Height = BitConverter.ToUInt16(data, 14),
                MipCount = data[16],
            };

            var blockCounts = Enumerable.Range(0, file.MipCount).Select(file.MipBlockCount).ToArray();
            var fourCC = Encoding.ASCII.GetString(data, 20, 4);
            var position = 24;
            var layerCount = BitCount(flags & 7);
            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                if (layerIndex > 0)
                {
                    fourCC = Encoding.ASCII.GetString(data, position, 4);
                    position += 4;
                }
                if (!Formats.TryGetValue(fourCC, out var format)) throw new InvalidDataException($"Unsupported FCTX layer format '{fourCC}'.");

                var layer = new FctxLayer { FourCC = fourCC, BlockSize = format.BlockSize, Mips = new byte[file.MipCount][] };
                for (var mip = file.MipCount - 1; mip >= 0; mip--)
                {
                    position += blockCounts[mip] * format.ExtraBlockSize;
                    var length = blockCounts[mip] * format.BlockSize;
                    if (position + length > data.Length) throw new InvalidDataException("FCTX body is shorter than its layer chain.");
                    layer.Mips[mip] = data.AsSpan(position, length).ToArray();
                    position += length;
                }
                file.Layers.Add(layer);
            }
            return file;
        }

        private static int BitCount(uint value)
        {
            var count = 0;
            for (; value != 0; value >>= 1) count += (int)(value & 1);
            return count;
        }
    }
}
