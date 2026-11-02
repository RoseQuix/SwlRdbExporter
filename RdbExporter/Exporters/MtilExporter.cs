using RdbExporter.Entities;
using RdbExporter.Parsers;
using System.Linq;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Writes MTIL map tile indexes (1010012) as JSON: per level its grid, tile size and each tile's [offset, size] in the
    /// 1010013 entry with the same ID, row-major. The last level is the largest (the client's mip 0).
    /// </summary>
    public class MtilExporter : JsonExporter
    {
        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data)
        {
            var index = MtilParser.Parse(data);
            return new
            {
                index.Version,
                index.Unknown,
                Levels = index.Levels.Select(l => new
                {
                    l.Columns,
                    l.Rows,
                    l.TileWidth,
                    l.TileHeight,
                    Tiles = l.Tiles.Select(t => new[] { t.Offset, t.Size }),
                }),
            };
        }
    }
}
