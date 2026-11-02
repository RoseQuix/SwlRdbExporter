using System;
using System.Collections.Generic;
using System.IO;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// MTIL map tile indexes (1010012), the tile pyramid of the 1010013 map entry with the same ID:
    ///   "MTI1" (version 1) or "MTIL" + u32 version; version &gt; 1: u32 (4 where tiles have a 4-pixel border)
    ///   u32 level count, per level {u32 columns, u32 rows, u32 tile width, u32 tile height, columns × rows × {u32 offset, u32 size}}
    /// Tiles are row-major, and the last level is the largest.
    /// </summary>
    public static class MtilParser
    {
        public record Level(int Columns, int Rows, int TileWidth, int TileHeight, (int Offset, int Size)[] Tiles);
        public record TileIndex(uint Version, uint? Unknown, List<Level> Levels);

        public static TileIndex Parse(byte[] data)
        {
            var reader = new BinaryReader(new MemoryStream(data));
            var magic = reader.ReadBytes(4).AsSpan();
            uint version;
            if (magic.SequenceEqual("MTI1"u8)) version = 1;
            else if (magic.SequenceEqual("MTIL"u8)) version = reader.ReadUInt32();
            else throw new InvalidDataException("Not an MTIL tile index.");
            uint? unknown = version > 1 ? reader.ReadUInt32() : null;

            var levelCount = reader.ReadInt32();
            var levels = new List<Level>();
            try
            {
                for (var i = 0; i < levelCount; i++)
                {
                    int columns = reader.ReadInt32(), rows = reader.ReadInt32(), width = reader.ReadInt32(), height = reader.ReadInt32();
                    var tiles = new (int, int)[columns * rows];
                    for (var t = 0; t < tiles.Length; t++) tiles[t] = (reader.ReadInt32(), reader.ReadInt32());
                    levels.Add(new Level(columns, rows, width, height, tiles));
                }
            }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException("MTIL tile index ends early.");
            }
            if (levels.Count == 0) throw new InvalidDataException("MTIL tile index has no levels.");
            if (reader.BaseStream.Position != data.Length) throw new InvalidDataException("MTIL tile index has trailing data.");
            return new TileIndex(version, unknown, levels);
        }
    }
}
