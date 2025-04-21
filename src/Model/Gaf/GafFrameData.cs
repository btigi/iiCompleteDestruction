namespace ii.CompleteDestruction.Model.Gaf;

public class GafFrameData
{
    public Int16 Width { get; set; }
    public Int16 Height { get; set; }
    public Int16 XOffset { get; set; }
    public Int16 YOffset { get; set; }
    public byte Unknown1 { get; set; }
    public byte CompressionMethod { get; set; }
    public Int16 NumberOfSubFrames { get; set; }
    public int Unknown2 { get; set; }
    public int OffsetToFrameData { get; set; }
    public int Unknown3 { get; set; }
}