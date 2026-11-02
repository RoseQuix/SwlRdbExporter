using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// Funcom LMXB binary XML (1010500-1010516).
    /// Header: "LMXB", i32 version (1), i32 root node offset, 8 bytes, i32 root count at 0x14. All offsets are relative to
    /// byte 8. A node is 16 bytes: i32 first child offset (-1 for a text node), i32 attribute offset, i32 name (or text)
    /// offset, u16 child count, u16 attribute count. Children and roots are consecutive nodes; an attribute is
    /// {i32 name offset, i32 value offset}. Strings are NUL-terminated UTF-8.
    /// Each type's bundle (ID 1) holds every record of the type as a separate root.
    /// </summary>
    public static class LmxbParser
    {
        private const int MaxDepth = 512;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool IsLmxb(byte[] data) => data.AsSpan().StartsWith("LMXB"u8);

        public static List<XElement> Parse(byte[] data)
        {
            if (!IsLmxb(data) || BitConverter.ToInt32(data, 4) != 1) throw new InvalidDataException("Not an LMXB version 1 file.");

            var rootOffset = BitConverter.ToInt32(data, 8);
            var rootCount = BitConverter.ToInt32(data, 0x14);
            var roots = new List<XElement>();
            for (var i = 0; i < rootCount; i++)
            {
                roots.Add((XElement)ReadNode(data, rootOffset + 16 * i, 0));
            }
            return roots;
        }

        private static XNode ReadNode(byte[] data, int offset, int depth)
        {
            if (depth > MaxDepth) throw new InvalidDataException("LMXB nesting too deep.");

            var position = 8 + offset;
            var firstChild = BitConverter.ToInt32(data, position);
            var attributeOffset = BitConverter.ToInt32(data, position + 4);
            var nameOffset = BitConverter.ToInt32(data, position + 8);
            var childCount = BitConverter.ToUInt16(data, position + 12);
            var attributeCount = BitConverter.ToUInt16(data, position + 14);

            if (firstChild == -1) return new XText(ReadString(data, nameOffset));

            var element = new XElement(ReadString(data, nameOffset));
            for (var i = 0; i < attributeCount; i++)
            {
                var attribute = 8 + attributeOffset + 8 * i;
                element.SetAttributeValue(ReadString(data, BitConverter.ToInt32(data, attribute)), ReadString(data, BitConverter.ToInt32(data, attribute + 4)));
            }
            for (var i = 0; i < childCount; i++)
            {
                element.Add(ReadNode(data, firstChild + 16 * i, depth + 1));
            }
            return element;
        }

        private static string ReadString(byte[] data, int offset)
        {
            var start = 8 + offset;
            var length = Array.IndexOf(data, (byte)0, start) - start;
            string value;
            try
            {
                value = Utf8.GetString(data, start, length);
            }
            catch (DecoderFallbackException)
            {
                value = Encoding.Latin1.GetString(data, start, length);
            }
            return ReplaceInvalidXmlChars(value);
        }

        /// <summary>
        /// A few strings hold control characters that XML can't represent (e.g. a stray 0x1F in two 1010516 material
        /// names). These become U+FFFD.
        /// </summary>
        private static string ReplaceInvalidXmlChars(string value)
        {
            char[] chars = null;
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsSurrogatePair(value, i)) i++;
                else if (!XmlConvert.IsXmlChar(value[i])) (chars ??= value.ToCharArray())[i] = '\uFFFD';
            }
            return chars == null ? value : new string(chars);
        }
    }
}
