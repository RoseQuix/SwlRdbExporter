using Google.Protobuf;
using RdbExporter.Entities;
using RdbExporter.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Decodes protobuf entries to JSON with the schemas recovered from the client (Protos/*.proto).
    /// Arguments[0] is the message name, e.g. "LoreTreeBuf" or "LuaScriptList".
    /// Some types mix formats (1000104 also holds XML), so entries that start with '&lt;' are written as .xml, and
    /// entries that don't parse as .dat.
    /// </summary>
    public class ProtobufExporter : IExporter
    {
        private static readonly JsonFormatter Formatter = new(JsonFormatter.Settings.Default.WithIndentation());

        public void RunExport(ExportParameters parameters)
        {
            if (parameters.Arguments.Count < 1) throw new ArgumentException("ProtobufExporter requires the message name as first argument.");

            var messageType = Type.GetType($"RdbExporter.Protos.{parameters.Arguments[0]}") ?? throw new ArgumentException($"Unknown protobuf message '{parameters.Arguments[0]}'.");
            var parser = (MessageParser)messageType.GetProperty("Parser").GetValue(null);
            var knownFieldsParser = parser.WithDiscardUnknownFields(true);

            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                var data = entry.ReadAllBytes(parameters.SwlInstallDir);
                if (data.Length > 0 && data[0] == '<')
                {
                    File.WriteAllBytes(Helpers.GetOutputPath(parameters, entry, ".xml", true), data);
                    return;
                }

                IMessage message;
                try
                {
                    message = parser.ParseFrom(data);
                }
                catch (InvalidProtocolBufferException e)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}' is not a valid {parameters.Arguments[0]}, writing it raw: {e.Message}");
                    File.WriteAllBytes(Helpers.GetOutputPath(parameters, entry, ".dat", true), data);
                    return;
                }

                //Generated Equals also compares unknown fields. (Comparing sizes doesn't work: some packed fields get written unpacked.)
                if (!message.Equals(knownFieldsParser.ParseFrom(data)))
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}' has fields the schema doesn't know; they are left out of the JSON.");
                }
                File.WriteAllText(Helpers.GetOutputPath(parameters, entry, ".json", true), Formatter.Format(message));
            });
        }
    }
}
