namespace ii.CompleteDestruction;

public class CrtFile
{
    public uint Signature { get; set; }
    public uint Unknown1 { get; set; }
    public uint NumberOfUnits { get; set; }
    public List<CrtUnit> Units { get; set; } = [];
    public uint NumberOfPlayers { get; set; }
    public List<CrtPlayer> Players { get; set; } = [];
    public uint NumberOfTriggers { get; set; }
    public List<CrtTrigger> Triggers { get; set; } = [];
}