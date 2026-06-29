namespace ii.CompleteDestruction.Model.Pco;

public class PcoFile
{
    public byte[] Signature { get; set; } = [];
    public int Reserved { get; set; }
    public short Width { get; set; }
    public short Height { get; set; }
    public short CanvasWidth { get; set; }
    public short CanvasHeight { get; set; }
    public List<(byte R, byte G, byte B)> Palette { get; set; } = [];
}