using SixLabors.ImageSharp;

namespace ii.CompleteDestruction.Model.Sct;

public class SctFile
{
    public Image Map { get; set; } = null!;
    public Image Minimap { get; set; } = null!;
    public List<HeightData> HeightInfo { get; set; } = [];
}

