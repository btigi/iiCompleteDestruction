using SixLabors.ImageSharp;

namespace ii.CompleteDestruction.Model.Sct;

public class SctFile
{
    // SCT format version (2 or 3). Retail TA sections use 3.
    public int Version { get; set; } = 3;

    public Image Map { get; set; } = null!;
    public Image Minimap { get; set; } = null!;
    public List<HeightData> HeightInfo { get; set; } = [];
}