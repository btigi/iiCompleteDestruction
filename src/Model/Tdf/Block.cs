namespace ii.CompleteDestruction.Model.Tdf;

public class Block
{
    public string SectionName { get; set; } = string.Empty;
    public List<Property> Properties { get; } = []; // Properties with their values and comments
    public string Comments { get; set; } = string.Empty; // Standalone comments within the block
    public List<Block> Blocks { get; } = [];
}
