using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Collision tag maps (1066609, RDBCollisionTagMap_c), id = playfield. Writes the per-metre cells as an 8-bit PNG
    /// (row = z; each cell is the dominant GRND terrain layer slot) and the 256 tags as JSON.
    /// Layout: "CTM1" or the inner header {1066609, id, version}; 256 × CollisionTag_t {u16 version (&lt;= 3), u8 footstep,
    /// [&gt;= 3: u8 occlusion], [&gt;= 2: u8 c]}; u32 width, height; width × height u8 cells, raw after "CTM1", zlib after the
    /// inner header (a short stream leaves the rest zero).
    /// </summary>
    public class CollisionTagMapExporter : IExporter
    {
        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    Process(parameters, entry);
                }
                catch (InvalidDataException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }

        private static void Process(ExportParameters parameters, IDBRIndexEntrty entry)
        {
            var data = entry.ReadAllBytes(parameters.SwlInstallDir, true);
            var r = new SpanReader(data);
            string form;
            uint? headerVersion = null;
            if (data.AsSpan().StartsWith("CTM1"u8))
            {
                form = "CTM1";
                r.Bytes(4);
            }
            else
            {
                if (r.U32() != entry.Type) throw new InvalidDataException("Neither CTM1 nor an inner header.");
                r.U32();
                headerVersion = r.U32();
                form = "zlib";
            }

            var tags = new List<object>();
            for (var i = 0; i < 256; i++)
            {
                var version = r.U16();
                if (version > 3) throw new InvalidDataException($"Unsupported CollisionTag version {version}.");
                var footstep = r.U8();
                byte? occlusion = version > 2 ? r.U8() : null;
                byte? c = version > 1 ? r.U8() : null;
                tags.Add(new { Slot = i, Version = version, Footstep = footstep, Occlusion = occlusion, C = c });
            }
            var width = (int)r.U32();
            var height = (int)r.U32();
            var cells = new byte[(long)width * height];
            if (form == "CTM1")
            {
                r.Bytes(cells.Length).CopyTo(cells);
                r.ExpectEnd();
            }
            else
            {
                using var zlib = new ZLibStream(new MemoryStream(data, r.Position, r.Remaining), CompressionMode.Decompress);
                var read = 0;
                for (int n; read < cells.Length && (n = zlib.Read(cells, read, cells.Length - read)) > 0;) read += n;
            }

            var pngPath = Helpers.GetOutputPath(parameters, entry, ".png", true);
            using (var output = File.Create(pngPath))
            {
                PngWriter.WriteGray8(output, width, height, cells);
            }
            var info = new { Form = form, HeaderVersion = headerVersion, Width = width, Height = height, Tags = tags };
            File.WriteAllText(Path.ChangeExtension(pngPath, ".json"), JsonSerializer.Serialize(info, JsonExporter.JsonOptions));
        }
    }
}
