using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Particle effects (1010207, RDBParticleSystem_c): u32 size, then a zlib-compressed "fastwire" blob read in place.
    /// An array is {u16 count, u16 stride, u32 offset from the descriptor}; a string is a u32 offset from the pointer to
    /// {u32 n, char[n], 0} (0 = none). Records only grow between versions: a field exists when it fits in the stride.
    /// Header: u8 version (&gt;= 10), f32 scale, f32 speedScale, 12 zero bytes, u8 flags, 3 zero bytes, then the top-level
    /// arrays. Lower-case field names are the editor's XML tags; names starting with an underscore are unknown.
    /// The parser checks that every byte of the blob belongs to the header, a record, an array or a string.
    /// </summary>
    public static class ParticleEffectParser
    {
        private abstract record Kind;
        private record Scalar(string Format) : Kind;
        private record Arr(object Element) : Kind;   //Field[], "curve", "pathkey", a scalar format, or null (unknown)
        private record Str : Kind;
        private record Rec(Field[] Spec, int Size) : Kind;
        private record Field(int Offset, string Name, Kind Kind);

        private static Field F(int offset, string name, string format) => new(offset, name, new Scalar(format));
        private static Field A(int offset, string name, object element) => new(offset, name, new Arr(element));
        private static IEnumerable<Field> Curves(IEnumerable<string> names, int at) => names.Select((n, k) => A(at + 8 * k, n, "curve"));

        private static readonly string[] EmitterCurves =
        {
            "emitterdirectionx", "emitterdirectiony", "emitterdirectionz", "velocityx", "velocityy", "velocityz", "ratemin", "ratemax",
            "lifespanmin", "lifespanmax", "sizemin", "sizemax", "anglemin", "anglemax", "lightfalloff", "lightcolorred",
            "lightcolorgreen", "lightcolorblue",
        };

        private static readonly Field[] Emitter = new[]
        {
            F(0x00, "relpos", "3f"), F(0x0c, "mindirectionscale", "f"), F(0x10, "maxdirectionscale", "f"), F(0x14, "direction", "3f"),
            F(0x20, "emittershape", "B"), F(0x21, "boxmin", "3f"), F(0x2d, "boxmax", "3f"), F(0x39, "sphereradius", "f"),
            F(0x3d, "emitterinnerradius", "f"), F(0x41, "emitterheight", "f"), F(0x45, "durationmin", "f"), F(0x49, "durationmax", "f"),
            F(0x4d, "loopcountmin", "i"), F(0x51, "loopcountmax", "i"), F(0x55, "maxparticles", "H"), F(0x57, "_b57", "B"),
            F(0x58, "_b58", "B"), F(0x59, "runphysreverse", "B"), F(0x5a, "align", "B"), F(0x5b, "impactdecal", "B"),
        }.Concat(Curves(EmitterCurves, 0x5c)).Concat(new[]
        {
            F(0xec, "pathtime", "f"), A(0xf0, "path", "pathkey"), F(0xf8, "pathmode", "B"),
            F(0xf9, "lighttype", "B"), F(0xfa, "_bfa", "B"), F(0xfb, "lighthotspotcone", "f"), F(0xff, "lightfalloffcone", "f"),
            F(0x103, "lightrotation", "3f"), F(0x10f, "lightrange", "f"), F(0x113, "_b113", "B"), F(0x114, "_b114", "B"),
            F(0x115, "_f115", "f"), F(0x119, "_b119", "B"), F(0x11a, "_f11a", "5f"), F(0x12e, "_has12f", "B"), F(0x12f, "_f12f", "3f"),
            F(0x13b, "_b13b", "B"),
        }).ToArray();

        private static readonly Field[] Particle =
        {
            F(0x00, "texture", "B"), F(0x01, "startangle", "h"), F(0x03, "endangle", "h"), F(0x05, "randomrotation", "B"),
            F(0x06, "randomxmirroring", "B"), F(0x07, "randomymirroring", "B"), F(0x08, "_b08", "B"), F(0x09, "flags", "B"),
            F(0x0a, "_b0a", "B"), F(0x0b, "gravitymultiplier", "f"), F(0x0f, "_f0f", "f"), F(0x13, "_f13", "f"),
            F(0x17, "airresistanceconstant", "f"), A(0x1b, "color", "4f"), A(0x23, "table", "4f"), F(0x2b, "usetable2", "B"),
            A(0x2c, "table2", "4f"),
        };

        private static readonly Field[] Texture =
        {
            new(0x00, "filename", new Str()), F(0x04, "rdbid", "I"), F(0x08, "playmode", "B"), F(0x09, "blendmode", "B"),
            F(0x0a, "minfps", "B"), F(0x0b, "maxfps", "B"), F(0x0c, "animrows", "B"), F(0x0d, "animcols", "B"),
            F(0x0e, "xoffset", "B"), F(0x0f, "yoffset", "B"), F(0x10, "random_start", "B"), F(0x11, "random_direction", "B"),
        };

        private static readonly Field[] Mesh =
        {
            F(0x00, "mesh_resourcetype", "I"), F(0x04, "mesh_resourceid", "I"), F(0x08, "skeleton_resourceid", "I"),
            F(0x0c, "startanim_resourceid", "I"), F(0x10, "loopanim_resourceid", "I"), F(0x14, "endanim_resourceid", "I"),
            F(0x18, "relpos", "3f"), F(0x24, "pathtime", "f"), A(0x28, "path", "pathkey"), F(0x30, "pathmode", "B"),
            F(0x31, "groundalingment", "B"), F(0x32, "gravitymultiplier", "f"), F(0x36, "airresistance_constant", "f"),
            F(0x3a, "xangle", "2f"), F(0x42, "yangle", "2f"), F(0x4a, "zangle", "2f"), F(0x52, "xspin", "2f"),
            F(0x5a, "yspin", "2f"), F(0x62, "zspin", "2f"), A(0x6a, "color", "4f"), A(0x72, "scale", "f"), F(0x7a, "flags", "B"),
        };

        private static readonly Field[] SystemSpec = { F(0, "startdelay", "f"), F(4, "emitter", "B"), F(5, "particle", "B") };
        private static readonly Field[] MeshSystem = { F(0, "startdelay", "f"), F(4, "emitter", "B"), F(5, "mesh", "B") };

        private static readonly Field[] Script =
        {
            F(0x00, "startdelay", "f"), F(0x04, "scriptid", "I"), F(0x08, "volumemultiplier", "f"), F(0x0c, "pitchoffset", "f"),
            F(0x10, "reverbmultiplier", "f"), F(0x14, "mindistmultiplier", "f"), F(0x18, "maxdistmultiplier", "f"),
            F(0x1c, "doppleroffset", "f"), F(0x20, "fadetime", "f"), A(0x24, "attributes", null), F(0x2c, "_tail", "12s"),
        };

        private static readonly Field[] Layer = new[] { F(0x00, "texture", "B"), F(0x01, "_values", "5f") }
            .Concat(Curves(Enumerable.Range(0, 7).Select(k => $"_c{k}"), 0x15)).ToArray();

        private static readonly Field[] Plane =
        {
            F(0x00, "start", "f"), F(0x04, "duration", "f"), F(0x08, "mesh", "B"),
            new(0x09, "color", new Rec(Layer, 0x4d)), new(0x56, "alpha", new Rec(Layer, 0x4d)),
        };

        private static readonly Field[] Decal = new[] { F(0x00, "enabled", "B"), F(0x01, "start", "f"), F(0x05, "duration", "f"), F(0x09, "texture", "B"), F(0x0a, "_b0a", "B") }
            .Concat(Curves(Enumerable.Range(0, 8).Select(k => $"_c{k}"), 0x0b))
            .Concat(Curves(Enumerable.Range(8, 4).Select(k => $"_c{k}"), 0x4b))
            .Append(F(0x6b, "flags", "I"))
            .Concat(Curves(new[] { "_c12" }, 0x6f)).ToArray();

        private static readonly Field[] Light =
        {
            F(0x00, "lighttype", "I"), F(0x04, "lighthotspotcone", "f"), F(0x08, "lightfalloffcone", "f"),
            F(0x0c, "lightrotation", "3f"), F(0x18, "lightrange", "f"),
        };

        private static readonly (int At, string Name, Field[] Spec)[] Slots =
        {
            (0x19, "systems", SystemSpec), (0x21, "meshsystems", MeshSystem), (0x29, "_unused29", null), (0x31, "emitters", Emitter),
            (0x39, "particles", Particle), (0x41, "textures", Texture), (0x49, "meshs", Mesh), (0x51, "scripts", Script),
            (0x59, "planes", Plane), (0x61, "decals", Decal),
        };
        //Only in the later version-10 layout; no reader in the client
        private static readonly (int At, string Name, Field[] Spec) LightsSlot = (0x69, "lights", Light);

        /// <summary>Inflates the entry (after the inner header): u32 size, zlib stream.</summary>
        public static byte[] Inflate(byte[] data)
        {
            var size = data.Length >= 6 ? BinaryPrimitives.ReadInt32LittleEndian(data) : -1;
            if (size <= 0 || size > 1 << 26) throw new InvalidDataException("Not a compressed particle effect.");
            var blob = new byte[size];
            int read = 0;
            bool trailing;
            try
            {
                using var zlib = new ZLibStream(new MemoryStream(data, 4, data.Length - 4), CompressionMode.Decompress);
                for (int n; read < size && (n = zlib.Read(blob, read, size - read)) > 0;) read += n;
                trailing = zlib.ReadByte() != -1;
            }
            catch (InvalidDataException)
            {
                throw new InvalidDataException("Not a zlib-compressed particle effect.");
            }
            if (read != size || trailing) throw new InvalidDataException("Inflated size doesn't match the header.");
            return blob;
        }

        public static Dictionary<string, object> Parse(byte[] blob)
        {
            var version = blob[0];
            if (version < 10) throw new InvalidDataException($"Version {version}, the client rejects anything below 10.");
            var reader = new Blob(blob);
            var headerLength = HeaderLength(blob);
            if (headerLength != 0x69 && headerLength != 0x6a && headerLength != 0x71) throw new InvalidDataException($"Header length 0x{headerLength:x}.");
            if (blob.AsSpan(9, 12).IndexOfAnyExcept((byte)0) >= 0 || blob.AsSpan(0x16, 3).IndexOfAnyExcept((byte)0) >= 0) throw new InvalidDataException("Non-zero header padding.");

            var effect = new Dictionary<string, object>
            {
                ["version"] = version,
                ["scale"] = BinaryPrimitives.ReadSingleLittleEndian(blob.AsSpan(1)),
                ["speedscale"] = BinaryPrimitives.ReadSingleLittleEndian(blob.AsSpan(5)),
                ["flags"] = blob[0x15],
            };
            var slots = headerLength == 0x71 ? Slots.Append(LightsSlot) : Slots;
            foreach (var (at, name, spec) in slots) effect[name] = reader.ReadArray(at, spec);
            if (headerLength == 0x6a) effect["_b69"] = blob[0x69];

            var position = headerLength;
            foreach (var (start, end) in reader.Spans.OrderBy(s => s.Start))
            {
                if (start != position) throw new InvalidDataException($"{(start > position ? "Gap" : "Overlap")} at 0x{position:x} (next block 0x{start:x}).");
                position = end;
            }
            if (position != blob.Length) throw new InvalidDataException($"Consumed {position} of {blob.Length} bytes.");
            return effect;
        }

        private static int HeaderLength(byte[] blob)
        {
            var version = blob[0];
            if (version >= 13) return 0x6a;
            if (version == 11) return 0x69;
            //Version 10 has two layouts: 0x69 bytes, or 0x71 with the lights array at 0x69
            var tops = Slots.Where(s => BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(s.At)) != 0)
                .Select(s => s.At + (int)BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(s.At + 4))).ToList();
            if (tops.Count > 0) return tops.Min() == 0x69 ? 0x69 : 0x71;
            return blob.Length;
        }

        private class Blob
        {
            private readonly byte[] _u;
            private readonly HashSet<int> _strings = new();

            public List<(int Start, int End)> Spans { get; } = new();

            public Blob(byte[] u) => _u = u;

            private ReadOnlySpan<byte> At(int offset, int length)
            {
                if (offset < 0 || offset + length > _u.Length) throw new InvalidDataException($"Read past the end at 0x{offset:x}.");
                return _u.AsSpan(offset, length);
            }

            public object ReadArray(int at, object element)
            {
                var count = BinaryPrimitives.ReadUInt16LittleEndian(At(at, 2));
                var stride = BinaryPrimitives.ReadUInt16LittleEndian(At(at + 2, 2));
                var offset = BinaryPrimitives.ReadUInt32LittleEndian(At(at + 4, 4));
                var items = new List<object>();
                if (count == 0) return items;
                var start = (int)(at + offset);
                if ((long)start + count * stride > _u.Length) throw new InvalidDataException($"Array at 0x{at:x} runs past the end.");
                Spans.Add((start, start + count * stride));
                switch (element)
                {
                    case null:
                        throw new InvalidDataException($"Array at 0x{at:x}: {count} elements of an unknown record type.");
                    case Field[] spec:
                        for (var k = 0; k < count; k++) items.Add(Record(start + k * stride, spec, stride));
                        break;
                    case "curve":
                        if (stride != 12) throw new InvalidDataException($"Curve stride {stride}.");
                        for (var k = 0; k < count; k++)
                        {
                            var v = Floats(start + 12 * k, 3);
                            items.Add(new Dictionary<string, object> { ["time"] = v[0], ["value"] = v[1], ["slope"] = v[2] });
                        }
                        break;
                    case "pathkey":
                        if (stride != 32) throw new InvalidDataException($"Path key stride {stride}.");
                        for (var k = 0; k < count; k++)
                        {
                            var v = Floats(start + 32 * k, 8);
                            items.Add(new Dictionary<string, object> { ["time"] = v[0], ["pos"] = v[1..4], ["rotation"] = v[4..8] });
                        }
                        break;
                    case string format:
                        var size = ScalarSize(format);
                        if (stride != size) throw new InvalidDataException($"Table stride {stride}, expected {size}.");
                        for (var k = 0; k < count; k++) items.Add(ReadScalar(start + size * k, format));
                        break;
                }
                return items;
            }

            private string String(int at)
            {
                var offset = BinaryPrimitives.ReadUInt32LittleEndian(At(at, 4));
                if (offset == 0) return null;
                var target = (int)(at + offset);
                var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(At(target, 4));
                if (At(target + 4 + length, 1)[0] != 0) throw new InvalidDataException($"String at 0x{target:x} isn't terminated.");
                if (_strings.Add(target)) Spans.Add((target, target + 5 + length)); //Identical strings are shared
                return Encoding.Latin1.GetString(At(target + 4, length));
            }

            private Dictionary<string, object> Record(int start, Field[] spec, int stride)
            {
                var record = new Dictionary<string, object>();
                var end = 0;
                foreach (var field in spec)
                {
                    var size = field.Kind switch { Rec rec => rec.Size, Arr => 8, Str => 4, Scalar s => ScalarSize(s.Format), _ => 0 };
                    if (field.Offset + size > stride) break;
                    if (field.Offset != end) throw new InvalidDataException($"Spec gap at 0x{field.Offset:x}.");
                    record[field.Name] = field.Kind switch
                    {
                        Rec rec => Record(start + field.Offset, rec.Spec, rec.Size),
                        Arr array => ReadArray(start + field.Offset, array.Element),
                        Str => String(start + field.Offset),
                        Scalar s => ReadScalar(start + field.Offset, s.Format),
                        _ => null,
                    };
                    end = field.Offset + size;
                }
                if (end != stride) throw new InvalidDataException($"Record stride {stride}, fields end at 0x{end:x}.");
                return record;
            }

            private float[] Floats(int at, int count)
            {
                var values = new float[count];
                for (var i = 0; i < count; i++) values[i] = BinaryPrimitives.ReadSingleLittleEndian(At(at + 4 * i, 4));
                return values;
            }

            private object ReadScalar(int at, string format)
            {
                var (count, type, hasCount) = SplitFormat(format);
                if (type == 's') return Convert.ToHexString(At(at, count)).ToLowerInvariant();
                var width = TypeSize(type);
                var values = new object[count];
                for (var i = 0; i < count; i++)
                {
                    var bytes = At(at + width * i, width);
                    values[i] = type switch
                    {
                        //Each arm boxed on its own, or the switch would convert every value to float
                        'f' => (object)BinaryPrimitives.ReadSingleLittleEndian(bytes),
                        'B' => (object)bytes[0],
                        'H' => (object)BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                        'h' => (object)BinaryPrimitives.ReadInt16LittleEndian(bytes),
                        'i' => (object)BinaryPrimitives.ReadInt32LittleEndian(bytes),
                        'I' => (object)BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                        _ => throw new ArgumentException($"Unknown format '{format}'."),
                    };
                }
                return hasCount ? values : values[0];
            }
        }

        private static (int Count, char Type, bool HasCount) SplitFormat(string format)
        {
            var digits = format.TakeWhile(char.IsDigit).Count();
            return (digits > 0 ? int.Parse(format[..digits]) : 1, format[digits], digits > 0);
        }

        private static int TypeSize(char type) => type switch { 'f' or 'i' or 'I' => 4, 'H' or 'h' => 2, _ => 1 };

        private static int ScalarSize(string format)
        {
            var (count, type, _) = SplitFormat(format);
            return count * TypeSize(type);
        }
    }
}
