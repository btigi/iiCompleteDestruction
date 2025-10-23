namespace ii.CompleteDestruction;

public partial class TaFileParser
{
    private class ParsedLine
    {
        public string Content { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
    }
}