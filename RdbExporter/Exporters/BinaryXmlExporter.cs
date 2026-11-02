using RdbExporter.Entities;
using RdbExporter.Parsers;
using RdbExporter.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// Converts LMXB binary XML to text XML. A file with several roots (each type's bundle, ID 1) gets them wrapped in a
    /// &lt;bundle&gt; element.
    /// </summary>
    public class BinaryXmlExporter : IExporter
    {
        private static readonly XmlWriterSettings Settings = new() { Indent = true };

        public void RunExport(ExportParameters parameters)
        {
            Parallel.ForEach(parameters.RdbFileEntries, entry =>
            {
                try
                {
                    var roots = LmxbParser.Parse(entry.ReadAllBytes(parameters.SwlInstallDir));
                    var document = new XDocument(roots.Count == 1 ? roots[0] : new XElement("bundle", roots));
                    using var writer = XmlWriter.Create(Helpers.GetOutputPath(parameters, entry, ".xml", true), Settings);
                    document.Save(writer);
                }
                catch (Exception e) when (e is InvalidDataException or XmlException or ArgumentException)
                {
                    Console.Error.WriteLine($"RDB ID '{entry.Id}': {e.Message}");
                }
            });
        }
    }
}
