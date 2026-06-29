using SixLabors.ImageSharp;

namespace ii.CompleteDestruction.Model.Gax;

public class GaxImage
{
    public Image Image { get; set; } = null!;
    public string TeamName { get; set; } = string.Empty;
    public int FrameIndex { get; set; }
}