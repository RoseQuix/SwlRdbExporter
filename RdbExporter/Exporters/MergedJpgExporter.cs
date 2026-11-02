using RdbExporter.Entities;
using RdbExporter.Parsers;
using RdbExporter.Utilities;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Stitches the zone maps (1010013): each entry is a pyramid of JPEG tiles, laid out by the MTIL tile index (1010012)
    /// with the same ID. The largest level is exported. Its tiles are drawn at the level's tile size, and in most maps
    /// the JPEGs carry a 4-pixel border on each side, so each tile is cropped around its centre.
    /// </summary>
    public class MergedJpgExporter : IExporter
    {
        private const int TileIndexType = 1010012;

        public void RunExport(ExportParameters parameters)
        {
            var tileIndexes = Helpers.GetRdbIndex(parameters.SwlInstallDir).Where(e => e.Type == TileIndexType).ToDictionary(e => e.Id);
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    if (!tileIndexes.TryGetValue(entry.Id, out var tileIndexEntry)) throw new InvalidDataException("No MTIL tile index.");
                    Process(parameters, entry, MtilParser.Parse(tileIndexEntry.ReadAllBytes(parameters.SwlInstallDir)));
                }
                catch (InvalidDataException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }

        private static void Process(ExportParameters parameters, IDBRIndexEntrty entry, MtilParser.TileIndex tileIndex)
        {
            var tiles = entry.ReadAllBytes(parameters.SwlInstallDir);
            var level = tileIndex.Levels[^1];

            using var finalImage = new SKBitmap(level.Columns * level.TileWidth, level.Rows * level.TileHeight);
            using (var canvas = new SKCanvas(finalImage))
            {
                for (var i = 0; i < level.Tiles.Length; i++)
                {
                    var (offset, size) = level.Tiles[i];
                    if ((long)offset + size > tiles.Length) throw new InvalidDataException($"Tile {i} lies outside the map data.");
                    using var image = SKBitmap.Decode(tiles.AsSpan(offset, size)) ?? throw new InvalidDataException($"Tile {i} is not a valid JPEG.");
                    var borderX = (image.Width - level.TileWidth) / 2;
                    var borderY = (image.Height - level.TileHeight) / 2;
                    var source = SKRect.Create(borderX, borderY, level.TileWidth, level.TileHeight);
                    var destination = SKRect.Create(i % level.Columns * level.TileWidth, i / level.Columns * level.TileHeight, level.TileWidth, level.TileHeight);
                    canvas.DrawBitmap(image, source, destination);
                }
            }

            using var output = File.Create(Path.Combine(parameters.ExportDirectory, $"{entry.Id}.jpg"));
            finalImage.Encode(output, SKEncodedImageFormat.Jpeg, 75);
        }
    }
}
