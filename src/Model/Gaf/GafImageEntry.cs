namespace ii.CompleteDestruction;

public partial class GafProcessor
{
    public class GafImageEntry
    {
        public string Name { get; set; } = string.Empty;
        public List<GafFrame> Frames { get; set; } = new();
    }
}