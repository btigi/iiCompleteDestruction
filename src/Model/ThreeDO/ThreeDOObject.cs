namespace ii.CompleteDestruction.Model.ThreeDO;

public class ThreeDOObject
{
    public int VersionSignature { get; set; }
    public int NumberOfVertexes { get; set; }
    public int NumberOfPrimitives { get; set; }
    public int SelectionPrimitive { get; set; }
    public int XFromParent { get; set; }
    public int YFromParent { get; set; }
    public int ZFromParent { get; set; }
    public int OffsetToObjectName { get; set; }
    public int Always_0 { get; set; }
    public int OffsetToVertexArray { get; set; }
    public int OffsetToPrimitiveArray { get; set; }
    public int OffsetToSiblingObject { get; set; }
    public int OffsetToChildObject { get; set; }

    // Resolved data
    public string Name { get; set; } = string.Empty;
    public ThreeDOVertex[] Vertices { get; set; } = [];
    public ThreeDOPrimitive[] Primitives { get; set; } = [];
    public ThreeDOObject? Child { get; set; }
    public ThreeDOObject? Sibling { get; set; }
}