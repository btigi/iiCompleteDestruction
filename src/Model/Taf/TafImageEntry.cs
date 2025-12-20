namespace ii.CompleteDestruction.Model.Taf;

public class TafImageEntry
{
    public string Name { get; set; } = string.Empty;
    public List<TafFrame> Frames { get; set; } = new();
}