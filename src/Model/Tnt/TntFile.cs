using SixLabors.ImageSharp;

namespace ii.CompleteDestruction;

public class TntFile
{
    public Image Map { get; set; }
    public Image Minimap { get; set; }
    public UInt32 SeaLevel { get; set; }
    public int AttributeWidth { get; set; }
    public int AttributeHeight { get; set; }
    public List<MapAttribute> MapAttributes { get; set; } = [];
    public List<TileAnimation> TileAnimations { get; set; } = [];

    // V2 - TA:K specific
    public byte[] HeightMap { get; set; } = [];
    public ushort[] FeatureIndices { get; set; } = [];
    public List<string> FeatureNames { get; set; } = [];
    public List<uint> TerrainNames { get; set; } = [];
    public byte[] UMapping { get; set; } = [];
    public byte[] VMapping { get; set; } = [];
}