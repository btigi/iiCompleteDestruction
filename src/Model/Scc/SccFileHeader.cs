namespace ii.CompleteDestruction.Model.Scc;

public class SccFileHeader
{
    public uint Signature { get; set; }
    public Guid DatabaseId { get; set; }
    public uint Checksum { get; set; }
    public uint ProjectId { get; set; }
    public uint FileCount { get; set; }
    public List<SccFileEntry> Entries { get; set; } = [];
}