using RdbExporter.Entities;
using RdbExporter.Protos;
using System.IO;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Writes the Lua script list (1010223, a LuaScriptList protobuf) as one ScriptID&lt;id&gt;.lua file per script, the
    /// module name scripts use to require each other, plus Index.json mapping each ID to its source (how the game uses
    /// it). Compiled scripts, if any, are written as .luac.
    /// </summary>
    public class LuaScriptExporter : IExporter
    {
        public void RunExport(ExportParameters parameters)
        {
            var index = new List<object>();
            foreach (var entry in parameters.RdbFileEntries)
            {
                var list = LuaScriptList.Parser.ParseFrom(entry.ReadAllBytes(parameters.SwlInstallDir));
                foreach (var script in list.Script)
                {
                    var path = Path.Combine(parameters.ExportDirectory, $"ScriptID{script.Id}");
                    if (script.HasRawScript) File.WriteAllText(path + ".lua", script.RawScript);
                    if (script.HasCompiledScript) File.WriteAllBytes(path + ".luac", script.CompiledScript.ToByteArray());
                    index.Add(new { script.Id, Source = script.Source.ToString() });
                }
            }

            var settings = new JsonSerializerOptions() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(Path.Combine(parameters.ExportDirectory, "Index.json"), JsonSerializer.Serialize(index, settings));
        }
    }
}
