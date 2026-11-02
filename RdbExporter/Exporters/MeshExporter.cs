using RdbExporter.Entities;
using RdbExporter.Parsers;
using RdbExporter.Utilities;
using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Converts FME meshes to Wavefront OBJ with positions, normals and the first UV set, one group and material per
    /// submesh. The .mtl points at the diffuse and normal maps where the textures export (1010004) puts them, so export
    /// that type next to this one to see them.
    /// </summary>
    public class MeshExporter : IExporter
    {
        private const int TextureType = 1010004;

        public void RunExport(ExportParameters parameters)
        {
            var textureParameters = new ExportParameters
            {
                SwlInstallDir = parameters.SwlInstallDir,
                ExportDirectory = Path.Combine("..", Helpers.GetExportDirectoryName(Helpers.GetKnownRdbType(TextureType.ToString()) ?? new RdbType { Id = TextureType })),
            };

            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    var submeshes = FmeParser.Parse(entry.ReadAllBytes(parameters.SwlInstallDir));
                    var objPath = Helpers.GetOutputPath(parameters, entry, ".obj", true);
                    WriteObj(objPath, submeshes);
                    WriteMtl(Path.ChangeExtension(objPath, ".mtl"), submeshes, textureParameters);
                }
                catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }

        private static string MaterialName(FmeSubmesh mesh, int index)
        {
            var name = mesh.Parameters.TryGetValue("g_Name", out var value) ? value : "";
            return $"submesh{index}" + (name.Length > 0 ? "_" + string.Concat(name.Select(c => char.IsWhiteSpace(c) ? '_' : c)) : "");
        }

        private static void WriteObj(string path, List<FmeSubmesh> submeshes)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine($"mtllib {Path.GetFileName(Path.ChangeExtension(path, ".mtl"))}");

            var vertexBase = 1;
            for (var k = 0; k < submeshes.Count; k++)
            {
                var mesh = submeshes[k];
                var positions = mesh.ReadAttribute(FmeSubmesh.UsagePosition, 3);
                var normals = mesh.ReadAttribute(FmeSubmesh.UsageNormal, 3);
                var uvs = mesh.ReadAttribute(FmeSubmesh.UsageTexCoord, 2);
                if (positions == null) continue;

                writer.WriteLine($"g submesh{k}");
                writer.WriteLine($"usemtl {MaterialName(mesh, k)}");
                foreach (var p in positions) writer.WriteLine($"v {F(p[0])} {F(p[1])} {F(p[2])}");
                if (uvs != null) foreach (var t in uvs) writer.WriteLine($"vt {F(t[0])} {F(1 - t[1])}");
                if (normals != null) foreach (var n in normals) writer.WriteLine($"vn {F(n[0])} {F(n[1])} {F(n[2])}");

                for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
                {
                    writer.Write('f');
                    for (var j = 0; j < 3; j++)
                    {
                        var v = mesh.Indices[i + j] + vertexBase;
                        writer.Write(uvs != null ? (normals != null ? $" {v}/{v}/{v}" : $" {v}/{v}") : (normals != null ? $" {v}//{v}" : $" {v}"));
                    }
                    writer.WriteLine();
                }
                vertexBase += mesh.VertexCount;
            }
        }

        private static void WriteMtl(string path, List<FmeSubmesh> submeshes, ExportParameters textureParameters)
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            for (var k = 0; k < submeshes.Count; k++)
            {
                var mesh = submeshes[k];
                writer.WriteLine($"newmtl {MaterialName(mesh, k)}");
                writer.WriteLine("Kd 1 1 1");
                WriteMap(writer, "map_Kd", mesh.GetTexture("g_DiffuseTex"), textureParameters);
                WriteMap(writer, "map_Bump", mesh.GetTexture("g_NormalTex"), textureParameters);
                writer.WriteLine();
            }
        }

        private static void WriteMap(StreamWriter writer, string statement, (int Type, int Id)? texture, ExportParameters textureParameters)
        {
            if (texture is not { Type: TextureType } t) return;
            var path = Helpers.GetOutputPath(textureParameters, new IDBRIndexEntrty { Type = t.Type, Id = t.Id }, ".dds", true);
            writer.WriteLine($"{statement} {path.Replace('\\', '/')}");
        }

        private static string F(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
