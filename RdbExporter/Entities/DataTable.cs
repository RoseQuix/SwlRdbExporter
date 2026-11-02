using System.Collections.Generic;

namespace RdbExporter.Entities
{
    public class DataTable
    {
        public uint? Version { get; set; }
        public List<object> Rows { get; set; }
    }
}
