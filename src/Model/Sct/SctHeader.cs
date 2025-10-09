namespace ii.CompleteDestruction.Model.Sct;

public class SctHeader
{
    public int Version { get; set; }
    public int PtrMinimap { get; set; }
    public int NumTiles { get; set; }
    public int PtrTiles { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int PtrData { get; set; }
}

