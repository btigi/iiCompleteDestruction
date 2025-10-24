using SixLabors.ImageSharp;

namespace ii.CompleteDestruction;

public class GafFrame
{
    public Image Image { get; set; } = null!;
    public short XOffset { get; set; } = 0;
    public short YOffset { get; set; } = 0;
    public bool UseCompression { get; set; } = false;
}