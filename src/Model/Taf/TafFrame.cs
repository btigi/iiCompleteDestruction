using SixLabors.ImageSharp;

namespace ii.CompleteDestruction.Model.Taf;

public class TafFrame
{
    public Image Image { get; set; } = null!;
    public short XOffset { get; set; } = 0;
    public short YOffset { get; set; } = 0;
    public TafPixelFormat PixelFormat { get; set; } = TafPixelFormat.Argb1555;
}