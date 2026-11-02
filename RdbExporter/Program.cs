using Microsoft.Extensions.CommandLineUtils;
using RdbExporter.Entities;
using RdbExporter.Exporters;
using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RdbExporter
{
    class Program
    {
        static int Main(string[] args)
        {
            var cmd = new CommandLineApplication();
            
            var listOption = cmd.Option("-l | --list", "List known RDB Types.",  CommandOptionType.NoValue);
            var listAllOption = cmd.Option("-la | --listAll", "List all RDB types, including unknown or types not used by SWL.", CommandOptionType.NoValue);
            var pathOption = cmd.Option("-i | --installDir <SWLPath>", "Path to SWL installation",  CommandOptionType.SingleValue);
            var rdbOption = cmd.Option("-d | --rdb <RDBNumberOrName>", "The name or ID of an RDB type to export; repeat for several. With --all, a type to leave out.",  CommandOptionType.MultipleValue);
            var allOption = cmd.Option("-a | --all", "Export every RDB type in the index, except those given with --rdb. Types without a configured exporter are exported as raw .dat files.", CommandOptionType.NoValue);
            var outOption = cmd.Option("-o | --out <OutputDir>", "Directory to write exports to (default: the current directory).", CommandOptionType.SingleValue);
            var rawOption = cmd.Option("-r | --raw", "Export raw .dat files instead of using the exporter type configured in RDBTypes.json.", CommandOptionType.NoValue);
            var dumpIndexOption = cmd.Option("-di | --dumpIndex", "Dump the RDB Index", CommandOptionType.NoValue);
            cmd.HelpOption("-? | -h | --help");

            cmd.OnExecute(() => {
                if (!pathOption.HasValue())
                {
                    Console.WriteLine("--installDir option is required.");
                    cmd.ShowHelp();
                    return 1;
                }

                if (listOption.HasValue() || listAllOption.HasValue())
                {
                    PrintIndex(pathOption.Value(), listAllOption.HasValue());
                    return 0;
                }

                var outputDir = Directory.CreateDirectory(outOption.HasValue() ? outOption.Value() : Directory.GetCurrentDirectory()).FullName;

                if (dumpIndexOption.HasValue())
                {
                    DumpIndex(pathOption.Value(), outputDir);
                    return 0;
                }

                if (!rdbOption.HasValue() && !allOption.HasValue())
                {
                    Console.WriteLine("--rdb, --all or --list option is required.");
                    cmd.ShowHelp();
                    return 1;
                }

                var rdbTypes = new List<RdbType>();
                foreach (var value in rdbOption.Values)
                {
                    var rdbType = ResolveRdbType(value, !allOption.HasValue());
                    if (rdbType == null) return 1;
                    rdbTypes.Add(rdbType);
                }

                if (!allOption.HasValue())
                {
                    var result = 0;
                    foreach (var rdbType in rdbTypes)
                    {
                        if (!ExportType(pathOption.Value(), outputDir, rdbType, rawOption.HasValue())) result = 1;
                    }
                    return result;
                }

                var excluded = rdbTypes.Select(t => t.Id).ToHashSet();
                var failed = new List<int>();
                foreach (var typeId in Helpers.GetRdbIndex(pathOption.Value()).Select(i => i.Type).Distinct().Where(t => !excluded.Contains(t)).OrderBy(t => t))
                {
                    var rdbType = Helpers.GetKnownRdbType(typeId.ToString()) ?? new RdbType() { Id = typeId };
                    try
                    {
                        if (!ExportType(pathOption.Value(), outputDir, rdbType, rawOption.HasValue())) failed.Add(typeId);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"Export of type {typeId} failed: {e}");
                        failed.Add(typeId);
                    }
                }
                if (failed.Count > 0)
                {
                    Console.Error.WriteLine($"Exports failed for types: {string.Join(", ", failed)}");
                    return 1;
                }
                return 0;
            });

            return cmd.Execute(args);
        }

        /// <summary>A known type by name or ID, or an unknown numeric ID (exported raw); null after printing an error.</summary>
        private static RdbType ResolveRdbType(string value, bool warnRaw)
        {
            var rdbType = Helpers.GetKnownRdbType(value);
            if (rdbType != null) return rdbType;
            if (int.TryParse(value, out int rdbId))
            {
                if (warnRaw) Console.WriteLine($"Known RdbType with name or ID '{value}' not found. Exporting as raw .dat files.");
                return new RdbType() { Id = rdbId };
            }
            Console.Error.WriteLine($"RdbType '{value}' is not a known named type and is an invalid number.");
            return null;
        }

        private static bool ExportType(string installDir, string outputDir, RdbType rdbType, bool raw)
        {
            var exportParameters = new ExportParameters()
            {
                RdbType = rdbType,
                Arguments = rdbType.ExporterArguments,
                SwlInstallDir = installDir,
                RdbFileEntries = Helpers.GetRdbIndex(installDir).Where(i => i.Type == rdbType.Id).ToList(),
            };

            if (exportParameters.RdbFileEntries.Count == 0)
            {
                Console.Error.WriteLine($"No entries found for RDB Type Id '{rdbType.Id}'.");
                return false;
            }
            exportParameters.ExportDirectory = Directory.CreateDirectory(Path.Combine(outputDir, Helpers.GetExportDirectoryName(rdbType))).FullName;

            if (!string.IsNullOrWhiteSpace(rdbType.ExporterType) && !raw)
            {
                Console.WriteLine("Running export for type:");
                WriteRrdbTypeLine(rdbType.Id, rdbType);
                var type = Type.GetType(rdbType.ExporterType);
                var exporter = Activator.CreateInstance(type) as IExporter;
                exporter.RunExport(exportParameters);
            }
            else
            {
                Console.WriteLine($"Exporting type {exportParameters.RdbType.Id} as raw .dat files.");
                exportParameters.Arguments = new List<string>{ ".dat" };
                new RawFileExporter().RunExport(exportParameters);
            }
            return true;
        }

        private static void PrintIndex(string installDir, bool listAll)
        {
            Console.WriteLine("Printing index of RDB Types");
            var rdbTypes = Helpers.GetRdbIndex(installDir);
            var types = rdbTypes.Select(r => r.Type).Distinct().Select(i => new { KnownType = Helpers.GetKnownRdbType(i.ToString()), TypeId = i });

            Console.WriteLine();
            Console.WriteLine("Types known to be used by SWL:");
            Console.WriteLine();
            foreach (var type in types.Where(t => t.KnownType?.IsSWL ?? false))
            {
                WriteRrdbTypeLine(type.TypeId, type.KnownType);
            }
            if (listAll)
            {
                Console.WriteLine();
                Console.WriteLine("Other types:");
                Console.WriteLine();
                foreach (var type in types.Where(t => !t.KnownType?.IsSWL ?? true))
                {
                    WriteRrdbTypeLine(type.TypeId, type.KnownType);
                }
            }
        }

        private static void WriteRrdbTypeLine(int typeId, RdbType knownType)
        {
            Console.Write($"Type ID: {typeId}");
            if (!string.IsNullOrWhiteSpace(knownType?.Name))
            {
                Console.Write($"   Name: '{ knownType.Name }'");
            }

            if (!string.IsNullOrWhiteSpace(knownType?.OtherDesc))
            {
                Console.Write($"    {knownType.OtherDesc}");
            }
            Console.WriteLine();
        }

        private static void DumpIndex(string installDir, string outputDir)
        {
            var settings = new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            var index = Helpers.GetRdbIndex(installDir);
            File.WriteAllText(Path.Combine(outputDir, "Index.json"), JsonSerializer.Serialize(index, settings));
        }
    }
}
