namespace ii.CompleteDestruction.Model.Tnt;

public class TntHeaderV2
{
    public uint Version { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint SeaLevel { get; set; }
    public uint HeightMapOffset { get; set; }
    public uint AttributesOffset { get; set; }
    public uint FeatureNamesOffset { get; set; }
    public uint FeatureCount { get; set; }
    public uint TerrainNamesOffset { get; set; }
    public uint UMappingOffset { get; set; }
    public uint VMappingOffset { get; set; }
    public uint MiniMapOffset { get; set; }
}