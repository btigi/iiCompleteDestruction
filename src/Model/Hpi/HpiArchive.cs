namespace ii.CompleteDestruction.Model.Hpi;

public class HpiArchive
{
    public List<HpiFileEntry> Files { get; set; } = [];
}

public class HpiFileEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
