using System.Collections.Generic;

namespace RdbExporter.Entities
{
    public class ItemTemplate
    {
        public uint Version { get; set; }
        public uint Kind { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<ItemStat> Stats { get; set; } = new();
        public List<ItemActionList> ActionLists { get; set; } = new();
        public List<ItemCriteriaItem> CriteriaItems { get; set; } = new();
        /// <summary>
        /// How each string was read, only set when some string isn't stored the way the client reads it ("bytes"):
        /// "chars" or "dotnet" mean the export tool wrote its UTF-16 length, and the client misreads the entry from there.
        /// </summary>
        public List<string> StringReadings { get; set; }
    }

    public record ItemStat(uint Stat, string Type, object Value);

    public class ItemActionList
    {
        public int Slot { get; set; }
        public List<ItemGameAction> Actions { get; set; } = new();
    }

    public class ItemGameAction
    {
        public uint Action { get; set; }
        public uint Unknown { get; set; }
        public uint Version { get; set; }
        public List<uint[]> Criteria { get; set; }
        public List<ItemStat> Stats { get; set; }
    }

    public class ItemCriteriaItem
    {
        public uint Key { get; set; }
        public List<uint[]> Criteria { get; set; }
    }
}
