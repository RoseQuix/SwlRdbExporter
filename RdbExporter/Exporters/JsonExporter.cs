using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Base for exporters that decode each entry of a binary type into an object and write it as JSON.
    /// </summary>
    public abstract class JsonExporter : IExporter
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        /// <summary>Serializer options; indented by default.</summary>
        protected virtual JsonSerializerOptions GetOptions(ExportParameters parameters) => JsonOptions;

        /// <summary>Whether Parse gets the entry with its inner header {type, id, version}, if it has one.</summary>
        protected virtual bool KeepInnerHeader(ExportParameters parameters) => false;

        /// <summary>Decodes one entry's payload; throws InvalidDataException if it doesn't parse.</summary>
        protected abstract object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data);

        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                var data = entry.ReadAllBytes(parameters.SwlInstallDir, KeepInnerHeader(parameters));
                object value;
                try
                {
                    value = Parse(parameters, entry, data);
                }
                catch (InvalidDataException e)
                {
                    if (Helpers.LooksLikeXml(data))
                    {
                        //A few binary types also hold misfiled XML sources
                        File.WriteAllBytes(Helpers.GetOutputPath(parameters, entry, ".xml", true), data);
                    }
                    else
                    {
                        Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                    }
                    return;
                }
                File.WriteAllText(Helpers.GetOutputPath(parameters, entry, ".json", true), JsonSerializer.Serialize(value, GetOptions(parameters)));
            });
        }
    }
}
