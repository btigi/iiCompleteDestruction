namespace ii.CompleteDestruction.Model.Tdf;

public class Block
{
    public string SectionName { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; } = [];
    public List<Block> Blocks { get; } = [];
}
