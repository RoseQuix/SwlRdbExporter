using System.IO;
using System.Text;

namespace RdbExporter.Utilities
{
    /// <summary>
    /// Writes block-compressed textures as DDS files with a legacy (non-DX10) header.
    /// </summary>
    public static class DdsWriter
    {
        private const int DDSD_CAPS = 0x1, DDSD_HEIGHT = 0x2, DDSD_WIDTH = 0x4, DDSD_PIXELFORMAT = 0x1000, DDSD_MIPMAPCOUNT = 0x20000, DDSD_LINEARSIZE = 0x80000;
        private const int DDPF_FOURCC = 0x4;
        private const int DDSCAPS_COMPLEX = 0x8, DDSCAPS_TEXTURE = 0x1000, DDSCAPS_MIPMAP = 0x400000;

        /// <param name="fourCC">DXT1, DXT5, ATI1 (BC4) or ATI2 (BC5).</param>
        /// <param name="mips">Block data per mip, largest first.</param>
        public static void Write(Stream output, int width, int height, string fourCC, byte[][] mips)
        {
            using var writer = new BinaryWriter(output, Encoding.ASCII, true);
            var hasMips = mips.Length > 1;

            writer.Write(Encoding.ASCII.GetBytes("DDS "));
            writer.Write(124);
            writer.Write(DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | DDSD_LINEARSIZE | (hasMips ? DDSD_MIPMAPCOUNT : 0));
            writer.Write(height);
            writer.Write(width);
            writer.Write(mips[0].Length);
            writer.Write(0); //Depth
            writer.Write(mips.Length);
            for (var i = 0; i < 11; i++) writer.Write(0); //Reserved

            //Pixel format
            writer.Write(32);
            writer.Write(DDPF_FOURCC);
            writer.Write(Encoding.ASCII.GetBytes(fourCC));
            for (var i = 0; i < 5; i++) writer.Write(0); //RGB bit count and masks

            writer.Write(DDSCAPS_TEXTURE | (hasMips ? DDSCAPS_COMPLEX | DDSCAPS_MIPMAP : 0));
            for (var i = 0; i < 4; i++) writer.Write(0); //Caps2-4, reserved

            foreach (var mip in mips)
            {
                writer.Write(mip);
            }
        }
    }
}
