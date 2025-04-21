namespace ii.CompleteDestruction.Model.Tdf;

public class Block
{
    public string SectionName { get; set; }
    public Dictionary<string, string> Properties { get; } = [];
    public List<Block> Blocks { get; } = [];
}
