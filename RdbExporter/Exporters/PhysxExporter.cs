using RdbExporter.Entities;
using RdbExporter.Parsers;
using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Converts PhysX collision collections (1000005, one per playfield) to
    ///   &lt;id&gt;.obj          every triangle mesh and box shape of every static actor in world units, one object per shape
    ///                     (actor&lt;index&gt;_&lt;geometry&gt;, with the shape's materials and filter data as a comment)
    ///   &lt;id&gt;_hf&lt;k&gt;.pfm    each heightfield, row = PhysX row = world x, column = world z; heights in world units
    ///                     (sample × heightScale) when a shape uses it, else the raw samples
    ///   &lt;id&gt;.json         materials (PhysX values plus the 1000637 material list entry they bind to) and the
    ///                     heightfields' placement
    /// A static shape's stored pose is already its world pose, so it is applied alone. Mesh vertices are scaled by
    /// PxMeshScale (R^T S R) first; heightfield vertex = (row × rowScale, height × heightScale, column × columnScale).
    /// </summary>
    public class PhysxExporter : IExporter
    {
        private const int MaterialListType = 1000637;

        public void RunExport(ExportParameters parameters)
        {
            var materialLists = Helpers.GetRdbIndex(parameters.SwlInstallDir)
                .Where(e => e.Type == MaterialListType)
                .ToDictionary(e => e.Id);
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    List<byte[]> materialList = null;
                    if (materialLists.TryGetValue(entry.Id, out var listEntry))
                    {
                        materialList = PhysxParser.ParseMaterialList(listEntry.ReadAllBytes(parameters.SwlInstallDir));
                    }
                    Process(parameters, entry, PhysxParser.Parse(entry.ReadAllBytes(parameters.SwlInstallDir)), materialList);
                }
                catch (InvalidDataException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }

        private static void Process(ExportParameters parameters, IDBRIndexEntrty entry, PhysxParser.Collection c, List<byte[]> materialList)
        {
            var basePath = Path.ChangeExtension(Helpers.GetOutputPath(parameters, entry, ".json", true), null);
            var objects = c.Objects;
            PhysxParser.PxObject Resolve(uint address) => c.ByAddress.TryGetValue(address, out var o)
                ? o
                : throw new InvalidDataException($"Unresolved reference {address}.");

            var materials = objects.Where(o => o.Type == PhysxParser.Material).ToList();
            var materialIndex = materials.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
            var heightFields = objects.Where(o => o.Type == PhysxParser.HeightField).ToList();
            var heightFieldShapes = new Dictionary<PhysxParser.PxObject, PhysxParser.PxObject>();

            using (var writer = new StreamWriter(basePath + ".obj", false, new UTF8Encoding(false)))
            {
                var vertexBase = 1L;
                for (var a = 0; a < objects.Count; a++)
                {
                    var actor = objects[a];
                    if (actor.Type != PhysxParser.RigidStatic) continue;
                    foreach (var shapeAddress in actor.Shapes)
                    {
                        var shape = Resolve(shapeAddress);
                        if (shape.Type != PhysxParser.Shape) throw new InvalidDataException($"Actor {a} refers to a non-shape object.");
                        double[] vertices;
                        uint[] triangles;
                        switch (shape.Geometry)
                        {
                            case PhysxParser.GeometryTriangleMesh:
                                var mesh = Resolve(shape.Mesh);
                                if (mesh.Type != PhysxParser.TriangleMesh) throw new InvalidDataException($"Actor {a}'s mesh shape refers to a non-mesh object.");
                                vertices = ScaleMesh(mesh.Vertices, shape.MeshScale, shape.MeshScaleRotation);
                                triangles = mesh.Triangles;
                                break;
                            case PhysxParser.GeometryHeightField:
                                var heightField = Resolve(shape.Mesh);
                                if (heightField.Type != PhysxParser.HeightField) throw new InvalidDataException($"Actor {a}'s heightfield shape refers to a non-heightfield object.");
                                heightFieldShapes[heightField] = shape;
                                continue;
                            default:
                                (vertices, triangles) = Box(shape.HalfExtents);
                                break;
                        }

                        writer.WriteLine($"o actor{a}_{PhysxParser.GeometryNames[(int)shape.Geometry]}");
                        writer.WriteLine($"# materials {string.Join(" ", shape.Materials.Select(m => materialIndex[Resolve(m)]))}"
                            + $" simulation filter {string.Join(" ", shape.SimulationFilter)} query filter {string.Join(" ", shape.QueryFilter)}");
                        var line = new StringBuilder();
                        for (var i = 0; i < vertices.Length; i += 3)
                        {
                            var v = Rotate(shape.Rotation, vertices[i], vertices[i + 1], vertices[i + 2]);
                            line.Clear().Append("v ").Append(F(v.X + shape.Position[0])).Append(' ').Append(F(v.Y + shape.Position[1])).Append(' ').Append(F(v.Z + shape.Position[2]));
                            writer.WriteLine(line);
                        }
                        for (var i = 0; i + 2 < triangles.Length; i += 3)
                        {
                            writer.WriteLine($"f {triangles[i] + vertexBase} {triangles[i + 1] + vertexBase} {triangles[i + 2] + vertexBase}");
                        }
                        vertexBase += vertices.Length / 3;
                    }
                }
            }

            var heightFieldInfo = new List<object>();
            for (var k = 0; k < heightFields.Count; k++)
            {
                var hf = heightFields[k];
                heightFieldShapes.TryGetValue(hf, out var shape);
                var scale = shape?.HeightScale ?? 1f;
                var samples = hf.Heights.Select(h => h * scale).ToArray();
                var file = $"{Path.GetFileName(basePath)}_hf{k}.pfm";
                using (var output = File.Create(Path.Combine(Path.GetDirectoryName(basePath), file)))
                {
                    PfmWriter.Write(output, hf.Columns, hf.Rows, 1, samples);
                }
                heightFieldInfo.Add(new
                {
                    File = file,
                    hf.Rows,
                    hf.Columns,
                    hf.Thickness,
                    Holes = hf.Material0.Count(m => (m & 0x7f) == PhysxParser.Hole) + hf.Material1.Count(m => (m & 0x7f) == PhysxParser.Hole),
                    HeightScale = shape?.HeightScale,
                    RowScale = shape?.RowScale,
                    ColumnScale = shape?.ColumnScale,
                    Rotation = shape?.Rotation,
                    Position = shape?.Position,
                    Materials = shape?.Materials.Select(m => materialIndex[Resolve(m)]).ToArray(),
                    SimulationFilter = shape?.SimulationFilter,
                    QueryFilter = shape?.QueryFilter,
                });
            }

            if (materialList != null && materialList.Count != materials.Count)
            {
                Console.Error.WriteLine($"RDB ID '{entry.Id}': {materials.Count} materials, the material list has {materialList.Count}.");
            }
            var info = new
            {
                c.Version,
                c.Platform,
                Actors = objects.Count(o => o.Type == PhysxParser.RigidStatic),
                Shapes = objects.Count(o => o.Type == PhysxParser.Shape),
                TriangleMeshes = objects.Count(o => o.Type == PhysxParser.TriangleMesh),
                Materials = materials.Select((m, i) => new
                {
                    m.DynamicFriction,
                    m.StaticFriction,
                    m.Restitution,
                    MaterialList = materialList != null && i < materialList.Count ? materialList[i].Select(b => (int)b).ToArray() : null,
                }),
                HeightFields = heightFieldInfo,
            };
            File.WriteAllText(basePath + ".json", JsonSerializer.Serialize(info, JsonExporter.JsonOptions));
        }

        /// <summary>PxMeshScale: R^T S R applied to each vertex, with R the scale rotation.</summary>
        private static double[] ScaleMesh(float[] vertices, float[] scale, float[] q)
        {
            var inverse = new[] { -q[0], -q[1], -q[2], q[3] };
            var result = new double[vertices.Length];
            for (var i = 0; i < vertices.Length; i += 3)
            {
                var v = Rotate(inverse, vertices[i], vertices[i + 1], vertices[i + 2]);
                v = Rotate(q, v.X * scale[0], v.Y * scale[1], v.Z * scale[2]);
                (result[i], result[i + 1], result[i + 2]) = v;
            }
            return result;
        }

        private static (double[], uint[]) Box(float[] half)
        {
            var vertices = new List<double>();
            foreach (var x in new[] { -half[0], half[0] })
            {
                foreach (var y in new[] { -half[1], half[1] })
                {
                    foreach (var z in new[] { -half[2], half[2] }) vertices.AddRange(new double[] { x, y, z });
                }
            }
            var triangles = new uint[] { 0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3 };
            return (vertices.ToArray(), triangles);
        }

        /// <summary>Rotates v by the quaternion q = (x, y, z, w).</summary>
        private static (double X, double Y, double Z) Rotate(IReadOnlyList<float> q, double vx, double vy, double vz)
        {
            double x = q[0], y = q[1], z = q[2], w = q[3];
            var tx = 2 * (y * vz - z * vy);
            var ty = 2 * (z * vx - x * vz);
            var tz = 2 * (x * vy - y * vx);
            return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx));
        }

        private static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
