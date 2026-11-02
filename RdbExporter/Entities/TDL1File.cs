using System.Collections.Generic;

namespace RdbExporter.Entities
{
    public class TDL1File
    {
        public string LanguageCode { get; set; }
        public string LanguageName { get; set; }
        public string LanguageNameInLanguage { get; set; }
        /// <summary>"(N == 1) /? 1 /: -1" in every file.</summary>
        public string PluralRule { get; set; }
        public List<TDL1Entry> Entries { get; set; } = new List<TDL1Entry>();
        public List<int> UnknownIds { get; set; } = new List<int>();
        public List<int> UnknownIds2 { get; set; } = new List<int>();
    }
}
