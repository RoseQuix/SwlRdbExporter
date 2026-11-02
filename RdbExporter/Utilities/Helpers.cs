using RdbExporter.Entities;
using RdbExporter.Parsers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RdbExporter.Utilities
{
    public class Helpers
    {
        private static object _lockObject = new object();
        private static Lazy<List<RdbType>> _knownRdbTypes = new Lazy<List<RdbType>>(GetKnownRdbTypes, true);
        private static List<IDBRIndexEntrty> _rdbIndex;
        private static Dictionary<int, Dictionary<int, string>> _filenameIndex;
        private static ConcurrentDictionary<int, HashSet<string>> _sharedFilenames = new ConcurrentDictionary<int, HashSet<string>>();

        public static List<IDBRIndexEntrty> GetRdbIndex(string installDir)
        {
            if (_rdbIndex != null) return _rdbIndex;

            lock (_lockObject)
            {
                if (_rdbIndex != null) return _rdbIndex;

                var rdbPath = Path.Combine(installDir, "RDB");
                return _rdbIndex = IBDRParser.ParseIBDRFile(Path.Combine(rdbPath, "le.idx"));
            }
        }

        private static List<RdbType> GetKnownRdbTypes()
        {
            var rdbTypesFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "RDBTypes.json");
            return JsonSerializer.Deserialize<List<RdbType>>(File.ReadAllText(rdbTypesFile), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true });
        }

        public static RdbType GetKnownRdbType(string nameOrId)
        {
            if (int.TryParse(nameOrId, out int id))
            {
                return _knownRdbTypes.Value.FirstOrDefault(r => r.Id == id);
            }
            return _knownRdbTypes.Value.FirstOrDefault(r => string.Equals(r.Name, nameOrId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Whether an entry looks like an XML document: "&lt;" and a letter or "?", and the first 64 bytes are printable
        /// text. Some binary types also hold misfiled XML sources, but binary data can start with "&lt;" too.
        /// </summary>
        public static bool LooksLikeXml(byte[] data)
        {
            if (data.Length < 2 || data[0] != '<' || !(data[1] == '?' || char.IsAsciiLetter((char)data[1]))) return false;
            return data.Take(64).All(b => b >= 0x20 && b < 0x7f || b == '\t' || b == '\r' || b == '\n');
        }

        /// <summary>
        /// Name of the folder a type is exported to, e.g. "1010004 - Textures".
        /// </summary>
        public static string GetExportDirectoryName(RdbType rdbType)
        {
            return string.IsNullOrWhiteSpace(rdbType.Name) ? rdbType.Id.ToString() : $"{rdbType.Id} - {rdbType.Name}";
        }

        /// <summary>
        /// Output path for an entry: its name from the filename index if it has one (with its extension changed to
        /// <paramref name="extension"/> if <paramref name="replaceNamedExtension"/> is set), otherwise its ID with <paramref name="extension"/>.
        /// Some names are shared by several IDs of a type, so those also get the ID appended.
        /// </summary>
        public static string GetOutputPath(ExportParameters parameters, IDBRIndexEntrty entry, string extension, bool replaceNamedExtension = false)
        {
            if (GetFilenameIndex(parameters.SwlInstallDir).TryGetValue(entry.Type, out var idToNameDictionary) && idToNameDictionary.TryGetValue(entry.Id, out var filename))
            {
                var sharedNames = _sharedFilenames.GetOrAdd(entry.Type, _ => idToNameDictionary.Values
                    .GroupBy(n => Path.GetFileNameWithoutExtension(n), StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
                var name = Path.GetFileNameWithoutExtension(filename);
                if (sharedNames.Contains(name))
                {
                    name += $".{entry.Id}";
                }
                return Path.Combine(parameters.ExportDirectory, name + (replaceNamedExtension ? extension : Path.GetExtension(filename)));
            }
            return Path.ChangeExtension(Path.Combine(parameters.ExportDirectory, entry.Id.ToString()), extension);
        }

        public static Dictionary<int, Dictionary<int, string>> GetFilenameIndex(string installDir)
        {
            if (_filenameIndex != null) return _filenameIndex;

            lock (_lockObject)
            {
                if (_filenameIndex != null) return _filenameIndex;

                var nameIndexRdb = GetRdbIndex(installDir).FirstOrDefault(i => i.Id == 1 && i.Type == 1000010);
                if (nameIndexRdb != null)
                {
                    return _filenameIndex = FilenameIndexParser.ParseNameIndex(nameIndexRdb.OpenEntryFile(installDir));
                }
                return _filenameIndex = new Dictionary<int, Dictionary<int, string>>();
            }
        }
    }
}
