using RdbExporter.Entities;
using System.IO;
using System.Collections.Generic;
using RdbExporter.Parsers;
using RdbExporter.Utilities;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    public class StringExporter : IExporter
    {
        public void RunExport(ExportParameters parameters)
        {
            ExportStrings(parameters);
        }

        private void ExportStrings(ExportParameters parameters)
        {
            var indexEntries = Helpers.GetRdbIndex(parameters.SwlInstallDir);
            var stringEntries = indexEntries.Where(i => i.Type == 1030002).ToDictionary(i => i.Id);

            var languageFiles = GetLanguageFiles(parameters.SwlInstallDir, indexEntries);

            var usedFiles = new HashSet<int>();
            foreach (var languageFile in languageFiles)
            {
                Parallel.ForEach(languageFile.Entries.Where(e => stringEntries.ContainsKey(e.TableId)), (languageEntry) =>
                    WriteOutputJson(parameters, stringEntries[languageEntry.TableId], languageEntry, languageFile)
                );

                foreach (var tableId in languageFile.Entries.Select(e => e.TableId))
                {
                    //Track which files we used
                    usedFiles.Add(tableId);
                }
            }

            Parallel.ForEach(indexEntries.Where(i => i.Type == 1030002 && !usedFiles.Contains(i.Id)), (rdbEntry) =>
                    WriteOutputJson(parameters, rdbEntry, null, null)
                );
        }

        /// <summary>
        /// The language files from the RDB (1030001), which cover more languages than the install's Data/text/*.tdbl and
        /// are newer; the Data/text files are the fallback.
        /// </summary>
        private static List<TDL1File> GetLanguageFiles(string installDir, List<IDBRIndexEntrty> indexEntries)
        {
            var rdbFiles = indexEntries.Where(i => i.Type == 1030001).Select(i => TDL1Parser.Parse(i.ReadAllBytes(installDir))).ToList();
            if (rdbFiles.Count > 0) return rdbFiles;

            //Linux installs may have the folder as "text", so match the name case-insensitively
            var dataDirectory = Path.Combine(installDir, "Data");
            var textDirectory = Directory.EnumerateDirectories(dataDirectory).FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Text", System.StringComparison.OrdinalIgnoreCase))
                ?? Path.Combine(dataDirectory, "Text");
            return Directory.EnumerateFiles(textDirectory, "*.tdbl", SearchOption.TopDirectoryOnly).Select(TDL1Parser.ParseTDL1File).ToList();
        }

        private void WriteOutputJson(ExportParameters parameters, IDBRIndexEntrty rdbIndexEntry, TDL1Entry languageEntry, TDL1File languageFile)
        {
            var tdc2File = TDC2Parser.ParseTDC2File(rdbIndexEntry.OpenEntryFile(parameters.SwlInstallDir));

            if (tdc2File.Entries.Count == 0) return;

            var settings = new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            var output = JsonSerializer.Serialize(tdc2File.Entries, settings);
            output = Regex.Replace(output, @"\\u[0-9A-F]{4}", " "); //Replace unicode endpoints, there are mostly variants of non-breaking spaces

            var outputPath = Path.Combine(parameters.ExportDirectory, languageFile?.LanguageName ?? "Unknown");
            Directory.CreateDirectory(outputPath);

            if (languageEntry != null)
            {
                outputPath = Path.Combine(outputPath, languageEntry.Name == null ? $"{languageEntry.Category}" : $"{languageEntry.Category}_{languageEntry.Name}");
            }
            else
            {
                outputPath = Path.Combine(outputPath, $"{tdc2File.Category}_{rdbIndexEntry.Id}");
            }
            outputPath = Path.ChangeExtension(outputPath, ".json");

            File.WriteAllText(outputPath, output);
        }
    }
}
