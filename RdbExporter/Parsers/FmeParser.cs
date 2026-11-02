using RdbExporter.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Funcom FME meshes (1010001), from Graphics_n::FMEMesh_c::LoadFromMemory in the client. A file is a sequence of
    /// submeshes read until EOF; each submesh's chunk loop ends at the next submesh's version word.
    /// Submesh:
    ///   u32 version (0x7000, 0x9002, 0x9003)
    ///   u32 texture count, then per texture u32 id (version &lt; 0x9000, type 1010004) or u32 type, u32 id
    ///   version &gt; 0x9001: u32, u32
    ///   u32 shader, u32 parameter length, parameter block (f32, u32, optional flag byte and up to 3 floats, then a
    ///     NUL-terminated "key=value ..." string: g_DiffuseTex=0 g_NormalTex=1 ... index the texture list)
    ///   u32 element count, that many D3DVERTEXELEMENT9 (last is D3DDECL_END), u32 stride
    ///   u32 vertex count, interleaved vertices; u32 triangle count, indices (u16 unless more than 0x10000 vertices)
    ///   tagged chunks: DPPM / INF0 / CUST {u32 version, u32 size, data}, CLUS {u32 version, [u32], u32 count, items}
    /// </summary>
    public static class FmeParser
    {
        private static readonly HashSet<uint> Versions = new() { 0x7000, 0x9002, 0x9003 };
        private static readonly Regex ParameterRegex = new(@"(\w+)=(""[^""]*""|\S+)", RegexOptions.Compiled);

        public static List<FmeSubmesh> Parse(byte[] data)
        {
            var reader = new BinaryReader(new MemoryStream(data));
            var submeshes = new List<FmeSubmesh>();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                submeshes.Add(ParseSubmesh(reader));
            }
            return submeshes;
        }

        private static FmeSubmesh ParseSubmesh(BinaryReader reader)
        {
            var mesh = new FmeSubmesh();
            var version = reader.ReadUInt32();
            if (version >= 0x9004) throw new InvalidDataException($"Unsupported FME version 0x{version:x}.");

            var textureCount = reader.ReadUInt32();
            if (textureCount > 0x3ff) throw new InvalidDataException($"Bad texture count {textureCount}.");
            for (var i = 0; i < textureCount; i++)
            {
                var type = version < 0x9000 ? 1010004 : reader.ReadInt32();
                mesh.Textures.Add((type, reader.ReadInt32()));
            }
            if (version > 0x9001) reader.ReadBytes(8);

            reader.ReadUInt32(); //Shader
            var parameterLength = reader.ReadInt32();
            if (parameterLength > 0x2000) throw new InvalidDataException($"Bad parameter block length {parameterLength}.");
            var parameterEnd = reader.BaseStream.Position + parameterLength;
            if (parameterLength >= 8)
            {
                reader.ReadBytes(8);
                if (parameterLength > 8)
                {
                    var flag = reader.ReadByte();
                    for (var i = 0; i < 3 && (flag == 0 || flag == 0xff) && parameterEnd - reader.BaseStream.Position > 3; i++)
                    {
                        reader.ReadSingle();
                    }
                }
            }
            var parameterString = reader.ReadBytes((int)(parameterEnd - reader.BaseStream.Position));
            var nul = Array.IndexOf(parameterString, (byte)0);
            foreach (Match match in ParameterRegex.Matches(Encoding.Latin1.GetString(parameterString, 0, nul < 0 ? parameterString.Length : nul)))
            {
                mesh.Parameters[match.Groups[1].Value] = match.Groups[2].Value.Trim('"');
            }

            var elementCount = reader.ReadUInt32();
            if (elementCount == 0 || elementCount > 16) throw new InvalidDataException($"Bad vertex element count {elementCount}.");
            for (var i = 0; i < elementCount; i++)
            {
                var stream = reader.ReadUInt16();
                var element = new FmeVertexElement { Offset = reader.ReadUInt16(), Type = reader.ReadByte() };
                reader.ReadByte(); //Method
                element.Usage = reader.ReadByte();
                element.UsageIndex = reader.ReadByte();
                if (stream == 0xff)
                {
                    if (i != elementCount - 1) throw new InvalidDataException("Vertex declaration ends early.");
                    break;
                }
                if (i == elementCount - 1) throw new InvalidDataException("Vertex declaration is not terminated.");
                mesh.Elements.Add(element);
            }
            mesh.Stride = reader.ReadInt32();
            mesh.VertexCount = reader.ReadInt32();
            mesh.Vertices = ReadExactly(reader, mesh.VertexCount * mesh.Stride);

            var indexCount = reader.ReadInt32() * 3;
            mesh.Indices = new int[indexCount];
            var wideIndices = mesh.VertexCount > 0x10000;
            for (var i = 0; i < indexCount; i++)
            {
                mesh.Indices[i] = wideIndices ? reader.ReadInt32() : reader.ReadUInt16();
            }

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var tag = reader.ReadUInt32();
                if (Versions.Contains(tag))
                {
                    reader.BaseStream.Seek(-4, SeekOrigin.Current);
                    break;
                }
                switch (Encoding.ASCII.GetString(BitConverter.GetBytes(tag)))
                {
                    case "DPPM":
                    case "INF0":
                    case "CUST":
                        reader.ReadUInt32();
                        ReadExactly(reader, reader.ReadInt32());
                        break;
                    case "CLUS":
                        var clusterVersion = reader.ReadUInt32();
                        if (clusterVersion == 2) reader.ReadUInt32();
                        else if (clusterVersion != 1) throw new InvalidDataException($"Unknown CLUS version {clusterVersion}.");
                        var clusterCount = reader.ReadUInt32();
                        for (var i = 0; i < clusterCount; i++)
                        {
                            reader.ReadBytes(7);
                            ReadExactly(reader, reader.ReadInt32());
                        }
                        break;
                    default:
                        throw new InvalidDataException($"Unknown FME chunk 0x{tag:x8} at 0x{reader.BaseStream.Position - 4:x}.");
                }
            }
            return mesh;
        }

        private static byte[] ReadExactly(BinaryReader reader, int count)
        {
            var bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new InvalidDataException("FME data ends early.");
            return bytes;
        }
    }
}
