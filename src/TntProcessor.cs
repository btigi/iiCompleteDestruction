using ii.CompleteDestruction.Model.Tnt;

namespace ii.CompleteDestruction;

public class TntProcessor : ITntProcessor
{
    private const int TileWidth = 32;
    private const int TileHeight = 32;

    public const ushort FeatureNone = 0xFFFF;
    public const ushort FeatureVoid = 0xFFFC;
    public const ushort FeatureUnknown = 0xFFFE;

    private ITntProcessor? _processor;

    public TntFile Read(string filePath, TaPalette palette)
    {
        _processor = CreateProcessor(filePath);
        return _processor.Read(filePath, palette);
    }

    public TntFile Read(byte[] data, TaPalette palette)
    {
        _processor = CreateProcessor(data);
        return _processor.Read(data, palette);
    }

    public TntFile Read(string filePath, TaPalette palette, Dictionary<string, byte[]> textures)
    {
        _processor = CreateProcessor(filePath);
        return _processor.Read(filePath, palette, textures);
    }

    public TntFile Read(byte[] data, TaPalette palette, Dictionary<string, byte[]> textures)
    {
        _processor = CreateProcessor(data);
        return _processor.Read(data, palette, textures);
    }

    public void Write(string filePath, TntFile tntFile, TaPalette palette)
    {
        Write(filePath, tntFile, palette, 1);
    }

    public void Write(string filePath, TntFile tntFile, TaPalette palette, int version)
    {
        ITntProcessor writer = version switch
        {
            1 => new TntProcessorV1(),
            2 => new TntProcessorV2(),
            _ => throw new ArgumentException($"Unsupported TNT version: {version}. Use 1 or 2.", nameof(version))
        };

        writer.Write(filePath, tntFile, palette);
    }

    private static ITntProcessor CreateProcessor(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        return CreateProcessorFromVersion(reader.ReadUInt32());
    }

    private static ITntProcessor CreateProcessor(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        return CreateProcessorFromVersion(reader.ReadUInt32());
    }

    private static ITntProcessor CreateProcessorFromVersion(uint version)
    {
        return version switch
        {
            TntProcessorV1.Version => new TntProcessorV1(), // 0x2000 - TA
            TntProcessorV2.Version => new TntProcessorV2(), // 0x4000 - TA:K
            _ => throw new InvalidDataException($"Unsupported TNT version: 0x{version:X4}")
        };
    }
}
