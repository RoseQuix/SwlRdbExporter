using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// PhysX 3.2 binary collections (1000005, RDBPhysicsCollection, one per playfield): the in-place format the client
    /// hands to PxCollection::deserialize. Alignment is relative to the entry start.
    ///   u32 version (0x00030002), char platform[4], u32 exported count + 16 B each, u32 imported count + 16 B each,
    ///   u32 object count, u32 old address count, u32 old addresses[], objects (each aligned to 16: u32 vtable,
    ///   u16 PxConcreteType, u16 flags, class body of the class's x86 size), then each object's extra data in the same
    ///   order (each aligned to 16), then u32 size + string table.
    /// Pointers hold old addresses: the k-th registered pointer is old address k, one per object and two per Material
    /// (shapes point at the second).
    /// </summary>
    public static class PhysxParser
    {
        public const int HeightField = 1, TriangleMesh = 3, RigidDynamic = 5, RigidStatic = 6, Shape = 7, Material = 8;
        public const int GeometryBox = 3, GeometryTriangleMesh = 5, GeometryHeightField = 6;
        public const int Hole = 0x7f;

        private static readonly Dictionary<int, int> ClassSizes = new()
        {
            [HeightField] = 0x78, [TriangleMesh] = 0x100, [RigidDynamic] = 0x130, [RigidStatic] = 0x6c, [Shape] = 0xd0, [Material] = 0x40,
        };

        public static readonly Dictionary<int, string> GeometryNames = new()
        {
            [0] = "sphere", [1] = "plane", [2] = "capsule", [3] = "box", [4] = "convex", [5] = "trianglemesh", [6] = "heightfield",
        };

        public class PxObject
        {
            public int Type;
            public int Offset;
            public ushort Flags;
            public uint Address;

            // HeightField: rows × columns {s16 height, u8 material0 | tess << 7, u8 material1}
            public int Rows, Columns;
            public float Thickness;
            public short[] Heights;
            public byte[] Material0, Material1;

            // TriangleMesh
            public float[] Vertices;
            public uint[] Triangles;

            // RigidStatic and Shape: pose quaternion (x, y, z, w) and position
            public float[] Rotation, Position;
            public uint[] Shapes;

            // Shape
            public uint Geometry;
            public float[] HalfExtents;
            public float[] MeshScale, MeshScaleRotation;
            public uint Mesh;
            public float HeightScale, RowScale, ColumnScale;
            public uint Actor;
            public uint[] SimulationFilter, QueryFilter;
            public uint[] Materials;

            // Material
            public float DynamicFriction, StaticFriction, Restitution;
        }

        public record Collection(uint Version, string Platform, List<PxObject> Objects, Dictionary<uint, PxObject> ByAddress);

        public static Collection Parse(byte[] data)
        {
            var r = new SpanReader(data);
            var version = r.U32();
            if (version != 0x00030002) throw new InvalidDataException($"Unsupported PhysX collection version 0x{version:x}.");
            var platform = r.Latin1(4);
            for (var k = 0; k < 2; k++) r.Bytes(16 * (int)r.U32());
            var count = (int)r.U32();
            var addresses = r.U32s((int)r.U32());

            var objects = new List<PxObject>();
            for (var i = 0; i < count; i++)
            {
                Align(r, 16);
                var start = r.Position;
                r.U32();
                var type = r.U16();
                if (!ClassSizes.TryGetValue(type, out var size)) throw new InvalidDataException($"Unhandled PhysX concrete type {type} at 0x{start:x}.");
                objects.Add(new PxObject { Type = type, Offset = start, Flags = r.U16() });
                r.Position = start;
                r.Bytes(size);
            }

            foreach (var o in objects)
            {
                Align(r, 16);
                switch (o.Type)
                {
                    case HeightField: ReadHeightField(data, r, o); break;
                    case TriangleMesh: ReadTriangleMesh(data, r, o); break;
                    case RigidStatic: ReadRigidStatic(data, r, o); break;
                    case Shape: ReadShape(data, r, o); break;
                    case Material: ReadMaterial(data, o); break;
                    default: throw new InvalidDataException($"PhysX concrete type {o.Type} at 0x{o.Offset:x} has unmapped extra data.");
                }
            }
            r.Bytes((int)r.U32());
            r.ExpectEnd();

            var byAddress = new Dictionary<uint, PxObject>();
            var next = 0;
            foreach (var o in objects)
            {
                var registered = o.Type == Material ? 2 : 1;
                if (next + registered > addresses.Length) throw new InvalidDataException($"{addresses.Length} old addresses are too few for the objects.");
                o.Address = addresses[next];
                for (var k = 0; k < registered; k++) byAddress[addresses[next++]] = o;
            }
            if (next != addresses.Length) throw new InvalidDataException($"{addresses.Length} old addresses, the objects register {next}.");
            return new Collection(version, platform, objects, byAddress);
        }

        /// <summary>RDBPhysicsMaterialList (1000637, same id as the collection): u8 version (1), u16 count, count × {u8, u8}.</summary>
        public static List<byte[]> ParseMaterialList(byte[] data)
        {
            var r = new SpanReader(data);
            var version = r.U8();
            if (version != 1) throw new InvalidDataException($"Unsupported material list version {version}.");
            var list = new List<byte[]>();
            for (var n = r.U16(); n > 0; n--) list.Add(r.Bytes(2).ToArray());
            r.ExpectEnd();
            return list;
        }

        private static void Align(SpanReader r, int alignment)
        {
            var pad = -r.Position & (alignment - 1);
            r.Bytes(pad);
        }

        private static SpanReader At(byte[] data, int offset) => new(data) { Position = offset };

        private static void ReadHeightField(byte[] data, SpanReader r, PxObject o)
        {
            var h = At(data, o.Offset + 0x10);
            o.Rows = (int)h.U32();
            o.Columns = (int)h.U32();
            o.Thickness = At(data, o.Offset + 0x28).F32();
            var n = o.Rows * o.Columns;
            o.Heights = new short[n];
            o.Material0 = new byte[n];
            o.Material1 = new byte[n];
            var samples = r.Bytes(4 * n);
            for (var i = 0; i < n; i++)
            {
                o.Heights[i] = BitConverter.ToInt16(samples[(4 * i)..]);
                o.Material0[i] = samples[4 * i + 2];
                o.Material1[i] = samples[4 * i + 3];
            }
        }

        private static void ReadTriangleMesh(byte[] data, SpanReader r, PxObject o)
        {
            // InternalTriangleMesh at +0x10, Ice::HybridModel at +0x20, Gu::RTree at +0x30
            var mesh = o.Offset + 0x10;
            var h = At(data, mesh);
            var vertexCount = (int)h.U32();
            var triangleCount = (int)h.U32();
            var hasVertices = h.U32() != 0;
            var hasTriangles = h.U32() != 0;
            var index16 = At(data, mesh + 0xac).U8() != 0;
            var extraTrig = At(data, mesh + 0xa8).U32() != 0;
            var materials = At(data, mesh + 0xb0).U32() != 0;
            var faceRemap = At(data, mesh + 0xb4).U32() != 0;
            var pages = (int)At(data, mesh + 0x20 + 0x50).U32();
            var leaves = At(data, mesh + 0x10 + 0x70).U32() & 0x7fffffff;

            Align(r, 128);
            r.Bytes(pages * 128);
            if (leaves > 1)
            {
                Align(r, 16);
                r.Bytes((int)leaves * 4);
            }
            if (hasVertices)
            {
                Align(r, 16);
                o.Vertices = r.F32s(vertexCount * 3);
            }
            o.Triangles = new uint[hasTriangles ? triangleCount * 3 : 0];
            if (hasTriangles)
            {
                Align(r, 16);
                for (var i = 0; i < o.Triangles.Length; i++) o.Triangles[i] = index16 ? r.U16() : r.U32();
            }
            foreach (var (present, size) in new[] { (extraTrig, 1), (materials, 2), (faceRemap, 4) })
            {
                if (!present) continue;
                Align(r, 16);
                r.Bytes(triangleCount * size);
            }
            o.Vertices ??= Array.Empty<float>();
            foreach (var index in o.Triangles)
            {
                if (index >= vertexCount) throw new InvalidDataException($"Triangle index {index} out of range in the mesh at 0x{o.Offset:x}.");
            }
        }

        private static uint[] ReadPtrTable(byte[] data, SpanReader r, int offset)
        {
            // Cm::PtrTable {u32 single pointer or list, u16 count, u8 ownsMemory, u8 bufferUsed}; a list is extra data
            var t = At(data, offset);
            var single = t.U32();
            var count = t.U16();
            if (count <= 1) return count == 1 ? new[] { single } : Array.Empty<uint>();
            Align(r, 16);
            return r.U32s(count);
        }

        private static void ReadRigidStatic(byte[] data, SpanReader r, PxObject o)
        {
            var h = At(data, o.Offset + 0x24);
            o.Rotation = h.F32s(4);
            o.Position = h.F32s(3);
            o.Shapes = ReadPtrTable(data, r, o.Offset + 0x1c);
            if (At(data, o.Offset + 0x18).U32() != 0) throw new InvalidDataException($"RigidStatic at 0x{o.Offset:x} has buffered state.");
        }

        private static void ReadShape(byte[] data, SpanReader r, PxObject o)
        {
            o.SimulationFilter = At(data, o.Offset + 0x30).U32s(4);
            var h = At(data, o.Offset + 0x40);
            o.Rotation = h.F32s(4);
            o.Position = h.F32s(3);
            o.Geometry = h.U32();
            var g = At(data, o.Offset + 0x60);
            switch (o.Geometry)
            {
                case GeometryBox:
                    o.HalfExtents = g.F32s(3);
                    break;
                case GeometryTriangleMesh:
                    o.MeshScale = g.F32s(3);
                    o.MeshScaleRotation = g.F32s(4);
                    o.Mesh = At(data, o.Offset + 0x80).U32();
                    break;
                case GeometryHeightField:
                    o.Mesh = g.U32();
                    o.HeightScale = g.F32();
                    o.RowScale = g.F32();
                    o.ColumnScale = g.F32();
                    break;
                default:
                    throw new InvalidDataException($"Unhandled geometry {o.Geometry} in the shape at 0x{o.Offset:x}.");
            }
            o.QueryFilter = At(data, o.Offset + 0xb0).U32s(4);
            o.Actor = At(data, o.Offset + 0xc0).U32();
            o.Materials = ReadPtrTable(data, r, o.Offset + 0xa4);
        }

        private static void ReadMaterial(byte[] data, PxObject o)
        {
            var h = At(data, o.Offset + 0x20);
            o.DynamicFriction = h.F32();
            o.StaticFriction = h.F32();
            o.Restitution = h.F32();
        }
    }
}
