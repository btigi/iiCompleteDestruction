namespace ii.CompleteDestruction.Model.Sct;

public class HeightData
{
    public byte Height { get; set; }
    public short Constant1 { get; set; }
    public byte Constant2 { get; set; }

    // V2-only trailing padding (4 bytes). Zero for V3 files and newly created entries.
    public uint Reserved { get; set; }
}