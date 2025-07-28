namespace ii.CompleteDestruction.Model.Tnt;

public class TntHeader
{
    public uint Version { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint MapDataOffset { get; set; }
    public uint MapAttributeOffset { get; set; }
    public uint MapTileOffset { get; set; }
    public uint TileCount { get; set; }
    public uint TileAnimationCount { get; set; }
    public uint TileAnimationOffset { get; set; }
    public uint SeaLevel { get; set; }
    public uint MinimapOffset { get; set; }
    public uint Unknown1 { get; set; }
    public uint Unknown2 { get; set; }
    public uint Unknown3 { get; set; }
    public uint Unknown4 { get; set; }
    public uint Unknown5 { get; set; }
}