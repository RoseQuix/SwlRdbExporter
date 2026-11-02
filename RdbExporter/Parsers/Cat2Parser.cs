using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Funcom "Cheetah-CAT2" skeletons (.cs, 1010002) and animations (.ca, 1010003).
    /// Skeleton: u32 version (0x10000-0x30000), u32 bone count, then per bone {u32 n, char name[n]; &gt;= 0x20000: u8
    ///   isAttractor (else: name contains "attractor"); f32 rotation[4] (x, y, z, w, local); f32 position[3] (local,
    ///   metres); u32 n, n × u32 child}; 0x30000: u8 mask[16].
    /// Animation: u32 version, f32 duration (s), u32 track count (&lt;= 0x200), then per track:
    ///   0x10000: u32 n, name; u32 n, n × {f32 time, f32 rotation[4], f32 position[3]}
    ///   0x20000: u32 n, name; u32 n, f32 time[n], f32 position[n][3]; u32 n, f32 time[n], f32 rotation[n][4]
    ///   0x30000: u32 FNV-1 hash of the bone name; u32 n, f32 position[n][3]; u32 n, i16 rotation[n][4] (/ 32767);
    ///            keys are 1/25 s apart
    /// Tracks bind to skeleton bones by name hash and replace the bone's local bind transform.
    /// </summary>
    public static class Cat2Parser
    {
        private const float V3KeyInterval = 0.04f;

        public record Bone(string Name, uint Hash, bool Attractor, float[] Rotation, float[] Position, int Parent, uint[] Children);
        public record Skeleton(uint Version, List<Bone> Bones, byte[] Mask);

        public class Track
        {
            public string Name { get; set; }
            public uint Hash { get; set; }
            /// <summary>{time, x, y, z} per key.</summary>
            public List<float[]> Positions { get; set; } = new();
            /// <summary>{time, x, y, z, w} per key.</summary>
            public List<float[]> Rotations { get; set; } = new();
        }

        public record Animation(uint Version, float Duration, List<Track> Tracks);

        public static uint Fnv1(string name)
        {
            var hash = 0x811c9dc5;
            foreach (var b in Encoding.Latin1.GetBytes(name)) hash = (hash * 0x1000193) ^ b;
            return hash;
        }

        public static Skeleton ParseSkeleton(byte[] data)
        {
            var r = new SpanReader(data);
            var version = r.U32();
            if (version != 0x10000 && version != 0x20000 && version != 0x30000) throw new InvalidDataException($"Unsupported skeleton version 0x{version:x}.");
            var count = (int)r.U32();
            var raw = new List<(string Name, bool Attractor, float[] Rotation, float[] Position, uint[] Children)>();
            for (var i = 0; i < count; i++)
            {
                var name = r.Latin1((int)r.U32());
                var attractor = version >= 0x20000 ? r.U8() != 0 : name.Contains("attractor");
                raw.Add((name, attractor, r.F32s(4), r.F32s(3), r.U32s((int)r.U32())));
            }
            var parents = new int[count];
            Array.Fill(parents, -1);
            for (var i = 0; i < count; i++)
            {
                foreach (var child in raw[i].Children)
                {
                    if (child < count) parents[child] = i;
                }
            }
            var mask = version >= 0x30000 ? r.Bytes(16).ToArray() : null;
            r.ExpectEnd();

            var bones = new List<Bone>();
            for (var i = 0; i < count; i++)
            {
                var (name, attractor, rotation, position, children) = raw[i];
                bones.Add(new Bone(name, Fnv1(name), attractor, rotation, position, parents[i], children));
            }
            return new Skeleton(version, bones, mask);
        }

        public static Animation ParseAnimation(byte[] data)
        {
            var r = new SpanReader(data);
            var version = r.U32();
            if (version != 0x10000 && version != 0x20000 && version != 0x30000) throw new InvalidDataException($"Unsupported animation version 0x{version:x}.");
            var duration = r.F32();
            var count = r.U32();
            if (count > 0x200) throw new InvalidDataException($"Track count {count} over the limit.");

            var tracks = new List<Track>();
            for (var i = 0; i < count; i++)
            {
                var track = new Track();
                if (version == 0x10000)
                {
                    track.Name = r.Latin1((int)r.U32());
                    for (var n = r.U32(); n > 0; n--)
                    {
                        var key = r.F32s(8);
                        track.Rotations.Add(new[] { key[0], key[1], key[2], key[3], key[4] });
                        track.Positions.Add(new[] { key[0], key[5], key[6], key[7] });
                    }
                }
                else if (version == 0x20000)
                {
                    track.Name = r.Latin1((int)r.U32());
                    var times = r.F32s((int)r.U32());
                    foreach (var t in times) track.Positions.Add(Prepend(t, r.F32s(3)));
                    times = r.F32s((int)r.U32());
                    foreach (var t in times) track.Rotations.Add(Prepend(t, r.F32s(4)));
                }
                else
                {
                    track.Hash = r.U32();
                    var positions = (int)r.U32();
                    for (var k = 0; k < positions; k++) track.Positions.Add(Prepend(k * V3KeyInterval, r.F32s(3)));
                    var rotations = (int)r.U32();
                    for (var k = 0; k < rotations; k++)
                    {
                        var q = r.Bytes(8);
                        track.Rotations.Add(new[] { k * V3KeyInterval, Packed(q, 0), Packed(q, 2), Packed(q, 4), Packed(q, 6) });
                    }
                }
                if (track.Name != null) track.Hash = Fnv1(track.Name);
                if (Math.Max(track.Positions.Count, track.Rotations.Count) > 0x1718) throw new InvalidDataException("Frame count over the limit.");
                tracks.Add(track);
            }
            r.ExpectEnd();
            return new Animation(version, duration, tracks);
        }

        private static float Packed(ReadOnlySpan<byte> q, int offset) => BitConverter.ToInt16(q[offset..]) * (1f / 32767);

        private static float[] Prepend(float time, float[] values)
        {
            var key = new float[values.Length + 1];
            key[0] = time;
            values.CopyTo(key, 1);
            return key;
        }
    }
}
