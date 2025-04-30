namespace WallstopStudios.Editor.DataVisualizer.Search
{
    using System.Collections.Generic;

    public class MatchDetail
    {
        public string FieldName { get; set; } // Use constants/enum for primary fields, property name otherwise
        public string MatchedValue { get; set; } // The full value of the field where the match occurred
        public List<string> MatchedTerms { get; set; } = new List<string>(); // Which search term(s) matched this field
    }
}
