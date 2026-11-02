using RdbExporter.Entities;
using RdbExporter.Utilities;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Face morph targets (1010007, RDBMorphFile_t) as JSON: per submesh of the head mesh, the decoded vertex offsets,
    /// plus bone records (named from the skeletons by hash). A head's .idx lists its morph files in &lt;MorphValues&gt;.
    /// Layout: u32 version (0x10000); u32 flags (bit 0: run-length coded groups; bit 1: bone records; bits 24-31: group
    /// count); groups × {u32 info (bits 16-31 scale, bits 8-15 submesh), u32 n (the submesh's vertex count), then
    /// either {u32 run, u32 value} runs summing to n or n × u32 value}; bone records if flagged and some group loaded:
    /// u32 m, m × {u32 bone hash, f32 rotation[4], f32 position[3]}.
    /// A value's bytes 0-2 are x, y, z: ((b + 0.5) / 255 × 2 - 1) × scale. The scale is 16-bit fixed point: bit 15 sign,
    /// bits 12-14 integer, bits 0-11 / 4096.
    /// </summary>
    public class MorphExporter : JsonExporter
    {
        private static readonly JsonSerializerOptions CompactOptions = new(JsonOptions) { WriteIndented = false };

        protected override JsonSerializerOptions GetOptions(ExportParameters parameters) => CompactOptions;

        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data)
        {
            var r = new SpanReader(data);
            var version = r.U32();
            if (version != 0x10000) throw new InvalidDataException($"Unsupported version 0x{version:x}.");
            var flags = r.U32();

            var groups = new List<object>();
            var loaded = false;
            for (var g = flags >> 24; g > 0; g--)
            {
                var info = r.U32();
                var count = (int)r.U32();
                var raw = new List<uint>(count);
                if ((flags & 1) != 0)
                {
                    while (raw.Count < count)
                    {
                        var run = r.U32();
                        var value = r.U32();
                        for (var k = 0; k < run && raw.Count < count; k++) raw.Add(value);
                    }
                }
                else
                {
                    raw.AddRange(r.U32s(count));
                }
                loaded |= count > 0;

                var scale = Scale((ushort)(info >> 16));
                var offsets = new float[raw.Count][];
                for (var i = 0; i < raw.Count; i++)
                {
                    offsets[i] = new float[3];
                    for (var k = 0; k < 3; k++) offsets[i][k] = (float)(((raw[i] >> (8 * k) & 0xff) + 0.5) / 255 * 2 * scale - scale);
                }
                groups.Add(new { Submesh = info >> 8 & 0xff, Low = info & 0xf, Scale = scale, VertexCount = count, Offsets = offsets });
            }

            var bones = new List<object>();
            if ((flags & 2) != 0 && (loaded || r.Remaining > 0))
            {
                var boneNames = Cat2Exporter.GetBoneNames(parameters.SwlInstallDir);
                for (var m = r.U32(); m > 0; m--)
                {
                    var hash = r.U32();
                    bones.Add(new { Hash = hash, Name = boneNames.GetValueOrDefault(hash), Rotation = r.F32s(4), Position = r.F32s(3) });
                }
            }
            return new { Flags = flags, Groups = groups, Bones = bones };
        }

        private static double Scale(ushort h) => ((h & 0x8000) != 0 ? -1 : 1) * ((h >> 12 & 7) + (h & 0xfff) / 4096.0);
    }
}
