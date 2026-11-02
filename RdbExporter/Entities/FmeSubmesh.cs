using System;
using System.Collections.Generic;
using System.IO;

namespace RdbExporter.Entities
{
    public class FmeSubmesh
    {
        public const int UsagePosition = 0, UsageNormal = 3, UsageTexCoord = 5;

        /// <summary>(RDB type, ID) of each texture; material parameters such as g_DiffuseTex index this list.</summary>
        public List<(int Type, int Id)> Textures { get; } = new();
        public Dictionary<string, string> Parameters { get; } = new();
        public List<FmeVertexElement> Elements { get; } = new();
        public int Stride { get; set; }
        public int VertexCount { get; set; }
        public byte[] Vertices { get; set; }
        public int[] Indices { get; set; }

        /// <summary>The texture a parameter such as g_DiffuseTex points at, if any.</summary>
        public (int Type, int Id)? GetTexture(string parameter)
        {
            if (Parameters.TryGetValue(parameter, out var value) && int.TryParse(value, out var index) && index >= 0 && index < Textures.Count)
            {
                return Textures[index];
            }
            return null;
        }

        /// <summary>
        /// The first <paramref name="components"/> values of an attribute for every vertex, or null if the mesh doesn't
        /// have it. UBYTE4N maps to [-1, 1], as normals and tangents use it.
        /// </summary>
        public float[][] ReadAttribute(int usage, int components, int usageIndex = 0)
        {
            var element = Elements.Find(e => e.Usage == usage && e.UsageIndex == usageIndex);
            if (element == null) return null;

            var values = new float[VertexCount][];
            for (var v = 0; v < VertexCount; v++)
            {
                var span = Vertices.AsSpan(v * Stride + element.Offset);
                var value = values[v] = new float[components];
                for (var c = 0; c < components; c++)
                {
                    value[c] = element.Type switch
                    {
                        0 or 1 or 2 or 3 => BitConverter.ToSingle(span[(4 * c)..]), //FLOAT1-4
                        8 => span[c] / 127.5f - 1, //UBYTE4N
                        9 => BitConverter.ToInt16(span[(2 * c)..]) / 32767f, //SHORT2N
                        15 or 16 => (float)BitConverter.ToHalf(span[(2 * c)..]), //FLOAT16_2, FLOAT16_4
                        _ => throw new InvalidDataException($"Unsupported vertex element type {element.Type}."),
                    };
                }
            }
            return values;
        }
    }

    public class FmeVertexElement
    {
        public int Offset { get; set; }
        /// <summary>D3DDECLTYPE</summary>
        public int Type { get; set; }
        /// <summary>D3DDECLUSAGE</summary>
        public int Usage { get; set; }
        public int UsageIndex { get; set; }
    }
}
