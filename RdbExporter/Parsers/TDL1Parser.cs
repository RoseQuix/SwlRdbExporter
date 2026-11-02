using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RdbExporter.Parsers
{
    /// <summary>
    /// TDL1 language files (1030001, and the older copies in Data/text/*.tdbl):
    ///   "TDL1", 4 × {i32 length, UTF-8, NUL}: language code, English name, native name, plural rule;
    ///   i32 count, count × {i32 category, i32 TDC2 table id (1030002)}; 2 zero bytes;
    ///   i32 count, count × {i32 category, i32 length + name, i32 length + column} (only some categories have one);
    ///   two lists of i32 count + ids, unknown (the first lists categories).
    /// </summary>
    public class TDL1Parser
    {
        public static TDL1File ParseTDL1File(string file) => Parse(File.ReadAllBytes(file));

        public static TDL1File Parse(byte[] data)
        {
            var r = new SpanReader(data);
            if (r.Latin1(4) != "TDL1") throw new InvalidDataException("Not a TDL1 file.");
            string ReadString(bool terminated)
            {
                var value = Encoding.UTF8.GetString(r.Bytes(r.I32()));
                if (terminated && r.U8() != 0) throw new InvalidDataException("String without its NUL terminator.");
                return value;
            }

            var file = new TDL1File
            {
                LanguageCode = ReadString(true),
                LanguageName = ReadString(true),
                LanguageNameInLanguage = ReadString(true),
                PluralRule = ReadString(true),
            };
            var tables = Enumerable.Range(0, r.I32()).Select(_ => (Category: r.I32(), TableId: r.I32())).ToList();
            if (r.U16() != 0) throw new InvalidDataException("Non-zero bytes after the table list.");
            var names = new Dictionary<int, (string Name, string Column)>();
            for (var n = r.I32(); n > 0; n--)
            {
                names[r.I32()] = (ReadString(false), ReadString(false));
            }
            file.UnknownIds = Enumerable.Range(0, r.I32()).Select(_ => r.I32()).ToList();
            file.UnknownIds2 = Enumerable.Range(0, r.I32()).Select(_ => r.I32()).ToList();
            r.ExpectEnd();

            file.Entries = tables.Select(t =>
            {
                names.TryGetValue(t.Category, out var name);
                return new TDL1Entry { Category = t.Category, TableId = t.TableId, Name = name.Name, Column = name.Column };
            }).ToList();
            var unlisted = names.Keys.Except(tables.Select(t => t.Category)).ToList();
            if (unlisted.Count > 0) throw new InvalidDataException($"Names for categories without a table: {string.Join(", ", unlisted)}.");
            return file;
        }
    }
}
