using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RdbExporter.Utilities
{
    /// <summary>
    /// Portable float maps: "Pf" (greyscale) or "PF" (RGB), little-endian, rows stored bottom to top.
    /// </summary>
    public static class PfmWriter
    {
        /// <param name="samples">width × height × channels floats, row by row from the top.</param>
        public static void Write(Stream output, int width, int height, int channels, ReadOnlySpan<float> samples)
        {
            if (channels != 1 && channels != 3) throw new ArgumentException("PFM holds 1 or 3 channels.");
            output.Write(Encoding.ASCII.GetBytes($"{(channels == 1 ? "Pf" : "PF")}\n{width} {height}\n-1.0\n"));
            var rowLength = width * channels;
            for (var y = height - 1; y >= 0; y--)
            {
                output.Write(MemoryMarshal.AsBytes(samples.Slice(y * rowLength, rowLength)));
            }
        }
    }
}
