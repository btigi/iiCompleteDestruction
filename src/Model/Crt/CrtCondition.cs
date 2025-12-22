namespace ii.CompleteDestruction;

public class CrtCondition
{
    public uint ConditionNumber { get; set; }
    public List<byte[]> Arguments { get; set; } = [];
    public List<string> ParsedArguments { get; set; } = [];
}