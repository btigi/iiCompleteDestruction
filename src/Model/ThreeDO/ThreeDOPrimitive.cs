namespace ii.CompleteDestruction.Model.ThreeDO;

public class ThreeDOPrimitive
{
    public int ColorIndex { get; set; }
    public int NumberOfVertexIndexes { get; set; }
    public int Always_0 { get; set; }
    public int OffsetToVertexIndexArray { get; set; }
    public int OffsetToTextureName { get; set; }
    public int Unknown_1 { get; set; }
    public int Unknown_2 { get; set; }
    public int Unknown_3 { get; set; }

    // Resolved data
    public short[] VertexIndices { get; set; } = [];
    public string? TextureName { get; set; }

    public PrimitiveType Type => NumberOfVertexIndexes switch
    {
        1 => PrimitiveType.Point,
        2 => PrimitiveType.Line,
        3 => PrimitiveType.Triangle,
        4 => PrimitiveType.Quad,
        _ => PrimitiveType.Unknown
    };

    public bool HasTexture => !string.IsNullOrEmpty(TextureName);
}

public enum PrimitiveType
{
    Unknown,
    Point,
    Line,
    Triangle,
    Quad
}