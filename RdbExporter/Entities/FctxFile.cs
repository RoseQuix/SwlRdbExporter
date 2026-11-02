using System;
using System.Collections.Generic;

namespace RdbExporter.Entities
{
    public class FctxFile
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int MipCount { get; set; }
        public List<FctxLayer> Layers { get; set; } = new List<FctxLayer>();

        public int MipBlockCount(int mip) => Math.Max(1, ((Width >> mip) + 3) / 4) * Math.Max(1, ((Height >> mip) + 3) / 4);
    }

    public class FctxLayer
    {
        /// <summary>DXT1, DXT5, ATI1, ATI2 or MIXD (BC5 with its unused extra blocks already dropped).</summary>
        public string FourCC { get; set; }
        public int BlockSize { get; set; }
        /// <summary>Block data per mip, largest (level 0) first.</summary>
        public byte[][] Mips { get; set; }
    }
}
