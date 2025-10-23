namespace ii.CompleteDestruction.Model.Tdf;

public class TaFile
{
    public List<Block> Blocks { get; } = [];
    public string HeaderComments { get; set; } = string.Empty; // Comments at the beginning of the file
}
