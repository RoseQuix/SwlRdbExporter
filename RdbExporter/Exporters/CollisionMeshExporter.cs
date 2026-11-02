using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Converts per-object collision meshes to OBJ, in the object's local space with one group per sub-mesh.
    /// 1010060 (RDBPhysicsTriangleMesh, the id is an .idx file's CollisionMesh2): u16 version (2), u16 count, then per
    ///   sub-mesh u8 m0, u8 m1 (probably footstep material and sound occlusion), u32 filter[4] (PhysX filter data),
    ///   u32 vertex count, u32 triangle count, vertices × 3 f32, triangles × 3 u32.
    /// 1010034 (older form, never loaded by the client): u32 vertex count, u32 index count, u16 material,
    ///   vertices × 3 f32, indices × u32.
    /// </summary>
    public class CollisionMeshExporter : IExporter
    {
        private record SubMesh(string Description, float[] Vertices, uint[] Indices);

        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    var data = entry.ReadAllBytes(parameters.SwlInstallDir);
                    var subMeshes = entry.Type == 1010034 ? ParseOld(data) : Parse(data);
                    WriteObj(Helpers.GetOutputPath(parameters, entry, ".obj", true), subMeshes);
                }
                catch (InvalidDataException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }

        private static List<SubMesh> Parse(byte[] data)
        {
            var r = new SpanReader(data);
            var version = r.U16();
            if (version != 2) throw new InvalidDataException($"Unsupported version {version}.");
            var subMeshes = new List<SubMesh>();
            for (var count = r.U16(); count > 0; count--)
            {
                var m0 = r.U8();
                var m1 = r.U8();
                var filter = r.U32s(4);
                var vertexCount = (int)r.U32();
                var triangleCount = (int)r.U32();
                subMeshes.Add(new SubMesh($"m0 {m0} m1 {m1} filter {string.Join(" ", filter)}", r.F32s(vertexCount * 3), r.U32s(triangleCount * 3)));
            }
            r.ExpectEnd();
            return subMeshes;
        }

        private static List<SubMesh> ParseOld(byte[] data)
        {
            var r = new SpanReader(data);
            var vertexCount = (int)r.U32();
            var indexCount = (int)r.U32();
            var material = r.U16();
            if (indexCount % 3 != 0) throw new InvalidDataException($"Index count {indexCount} isn't a multiple of 3.");
            var subMesh = new SubMesh($"material {material}", r.F32s(vertexCount * 3), r.U32s(indexCount));
            r.ExpectEnd();
            return new List<SubMesh> { subMesh };
        }

        private static void WriteObj(string path, List<SubMesh> subMeshes)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            var vertexBase = 1;
            for (var k = 0; k < subMeshes.Count; k++)
            {
                var mesh = subMeshes[k];
                writer.WriteLine($"# {mesh.Description}");
                writer.WriteLine($"g submesh{k}");
                for (var i = 0; i < mesh.Vertices.Length; i += 3)
                {
                    writer.WriteLine($"v {F(mesh.Vertices[i])} {F(mesh.Vertices[i + 1])} {F(mesh.Vertices[i + 2])}");
                }
                for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                {
                    writer.WriteLine($"f {mesh.Indices[i] + vertexBase} {mesh.Indices[i + 1] + vertexBase} {mesh.Indices[i + 2] + vertexBase}");
                }
                vertexBase += mesh.Vertices.Length / 3;
            }
        }

        private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
