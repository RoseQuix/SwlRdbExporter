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

        /// <summary>Decodes one entry's payload; throws InvalidDataException if it doesn't parse.</summary>
        protected abstract object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data);

        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    var value = Parse(parameters, entry, entry.ReadAllBytes(parameters.SwlInstallDir));
                    File.WriteAllText(Helpers.GetOutputPath(parameters, entry, ".json", true), JsonSerializer.Serialize(value, JsonOptions));
                }
                catch (InvalidDataException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }
    }
}
