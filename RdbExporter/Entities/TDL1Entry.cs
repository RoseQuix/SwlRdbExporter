namespace RdbExporter.Entities
{
    public class TDL1Entry
    {
        /// <summary>The string category.</summary>
        public int Category { get; set; }
        /// <summary>The category's TDC2 table (1030002) in this language.</summary>
        public int TableId { get; set; }
        /// <summary>Only some categories have a name and column.</summary>
        public string Name { get; set; }
        public string Column { get; set; }
    }
}
