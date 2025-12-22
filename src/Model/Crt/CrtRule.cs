namespace ii.CompleteDestruction;

public class CrtRule
{
    public uint NumberOfConditions { get; set; }
    public List<CrtCondition> Conditions { get; set; } = [];
    public uint NumberOfActions { get; set; }
    public List<CrtAction> Actions { get; set; } = [];
}