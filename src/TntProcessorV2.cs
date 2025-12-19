using ii.CompleteDestruction.Model.Tnt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

namespace ii.CompleteDestruction;

public class TntProcessorV2 : TntProcessorBase
{
    public const uint Version = 0x4000;
    private const int DataUnitSize = 16;
    private const int GraphicUnitSize = 32;

    public override TntFile Read(string filePath, TaPalette palette)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br, palette, null);
    }

    public override TntFile Read(byte[] data, TaPalette palette)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br, palette, null);
    }

    public override TntFile Read(string filePath, TaPalette palette, Dictionary<string, byte[]> textures)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br, palette, textures);
    }

    public override TntFile Read(byte[] data, TaPalette palette, Dictionary<string, byte[]> textures)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br, palette, textures);
    }

    private TntFile Read(BinaryReader br, TaPalette palette, Dictionary<string, byte[]>? textures)
    {
        var result = new TntFile();
        var header = ReadHeader(br);

        // Height map
        br.BaseStream.Seek(header.HeightMapOffset, SeekOrigin.Begin);
        var heightMapSize = (int)(header.Width * header.Height);
        result.HeightMap = br.ReadBytes(heightMapSize);

        // Attributes (width * height shorts, feature indices or special values)
        br.BaseStream.Seek(header.AttributesOffset, SeekOrigin.Begin);
        result.FeatureIndices = new ushort[heightMapSize];
        for (int i = 0; i < heightMapSize; i++)
        {
            result.FeatureIndices[i] = br.ReadUInt16();
        }

        // Feature names
        br.BaseStream.Seek(header.FeatureNamesOffset, SeekOrigin.Begin);
        result.FeatureNames = new List<string>((int)header.FeatureCount);
        for (int i = 0; i < header.FeatureCount; i++)
        {
            var index = br.ReadInt32();
            var nameBytes = br.ReadBytes(128);
            var name = Encoding.ASCII.GetString(nameBytes).Split('\0')[0];
            result.FeatureNames.Add(name);
        }

        // Calculate graphic unit dimensions for terrain/UV data
        var guWidth = (int)(header.Width * DataUnitSize / GraphicUnitSize);
        var guHeight = (int)(header.Height * DataUnitSize / GraphicUnitSize);
        var guCount = guWidth * guHeight;

        // Terrain names
        br.BaseStream.Seek(header.TerrainNamesOffset, SeekOrigin.Begin);
        result.TerrainNames = new List<uint>(guCount);
        for (int i = 0; i < guCount; i++)
        {
            result.TerrainNames.Add(br.ReadUInt32());
        }

        // U mapping (one byte per Graphic Unit)
        br.BaseStream.Seek(header.UMappingOffset, SeekOrigin.Begin);
        result.UMapping = br.ReadBytes(guCount);

        // V mapping (one byte per Graphic Unit)
        br.BaseStream.Seek(header.VMappingOffset, SeekOrigin.Begin);
        result.VMapping = br.ReadBytes(guCount);

        // minimap
        br.BaseStream.Seek(header.MiniMapOffset, SeekOrigin.Begin);
        var minimapWidth = br.ReadInt32();
        var minimapHeight = br.ReadInt32();
        var minimapData = br.ReadBytes(minimapWidth * minimapHeight);
        result.Minimap = ProcessMinimapFromRawData(minimapData, minimapWidth, minimapHeight, palette);

        result.AttributeWidth = (int)header.Width;
        result.AttributeHeight = (int)header.Height;
        result.SeaLevel = header.SeaLevel;

        // Render the map if we have the graphics, otherwise render a heightmap as the best we can do
        if (textures != null && textures.Count > 0)
        {
            result.Map = RenderMapFromTextures(result.TerrainNames, result.UMapping, result.VMapping,
                guWidth, guHeight, textures);
        }
        else
        {
            result.Map = CreateHeightmapVisualization(result.HeightMap, (int)header.Width, (int)header.Height);
        }

        return result;
    }

    public override void Write(string filePath, TntFile tntFile, TaPalette palette)
    {
        using var bw = new BinaryWriter(File.Open(filePath, FileMode.Create));

        var width = (uint)tntFile.AttributeWidth;
        var height = (uint)tntFile.AttributeHeight;
        var heightMapSize = (int)(width * height);

        var guWidth = (int)(width * DataUnitSize / GraphicUnitSize);
        var guHeight = (int)(height * DataUnitSize / GraphicUnitSize);
        var guCount = guWidth * guHeight;

        var heightMap = tntFile.HeightMap.Length == heightMapSize ? tntFile.HeightMap : new byte[heightMapSize];
        var featureIndices = tntFile.FeatureIndices.Length == heightMapSize ? tntFile.FeatureIndices : Enumerable.Repeat(FeatureNone, heightMapSize).ToArray();
        var terrainNames = tntFile.TerrainNames.Count == guCount ? tntFile.TerrainNames : Enumerable.Repeat(0u, guCount).ToList();
        var uMapping = tntFile.UMapping.Length == guCount ? tntFile.UMapping : new byte[guCount];
        var vMapping = tntFile.VMapping.Length == guCount ? tntFile.VMapping : new byte[guCount];

        // Calculate offsets
        const uint headerSize = 48; // 12 longwords * 4 bytes
        var heightMapOffset = headerSize;
        var attributesOffset = heightMapOffset + (uint)heightMapSize;
        var featureNamesOffset = attributesOffset + (uint)(heightMapSize * 2);
        var featureNamesSize = (uint)(tntFile.FeatureNames.Count * 132); // 4 bytes index + 128 bytes name
        var terrainNamesOffset = featureNamesOffset + featureNamesSize;
        var terrainNamesSize = (uint)(guCount * 4);
        var uMappingOffset = terrainNamesOffset + terrainNamesSize;
        var vMappingOffset = uMappingOffset + (uint)guCount;
        var miniMapOffset = vMappingOffset + (uint)guCount;

        var header = new TntHeaderV2
        {
            Version = Version,
            Width = width,
            Height = height,
            SeaLevel = tntFile.SeaLevel,
            HeightMapOffset = heightMapOffset,
            AttributesOffset = attributesOffset,
            FeatureNamesOffset = featureNamesOffset,
            FeatureCount = (uint)tntFile.FeatureNames.Count,
            TerrainNamesOffset = terrainNamesOffset,
            UMappingOffset = uMappingOffset,
            VMappingOffset = vMappingOffset,
            MiniMapOffset = miniMapOffset
        };
        WriteHeader(bw, header);

        bw.Write(heightMap);

        foreach (var index in featureIndices)
        {
            bw.Write(index);
        }

        for (int i = 0; i < tntFile.FeatureNames.Count; i++)
        {
            bw.Write(i);
            var nameBytes = new byte[128];
            var sourceBytes = Encoding.ASCII.GetBytes(tntFile.FeatureNames[i]);
            Array.Copy(sourceBytes, nameBytes, Math.Min(sourceBytes.Length, 127));
            bw.Write(nameBytes);
        }

        // Write terrain names
        foreach (var terrainName in terrainNames)
        {
            bw.Write(terrainName);
        }

        // Write UV mapping
        bw.Write(uMapping);
        bw.Write(vMapping);

        // Write minimap
        var minimapBytes = ImageToPaletteIndices(tntFile.Minimap, palette);
        bw.Write(tntFile.Minimap.Width);
        bw.Write(tntFile.Minimap.Height);
        bw.Write(minimapBytes);
    }

    private static void WriteHeader(BinaryWriter writer, TntHeaderV2 header)
    {
        writer.Write(header.Version);
        writer.Write(header.Width);
        writer.Write(header.Height);
        writer.Write(header.SeaLevel);
        writer.Write(header.HeightMapOffset);
        writer.Write(header.AttributesOffset);
        writer.Write(header.FeatureNamesOffset);
        writer.Write(header.FeatureCount);
        writer.Write(header.TerrainNamesOffset);
        writer.Write(header.UMappingOffset);
        writer.Write(header.VMappingOffset);
        writer.Write(header.MiniMapOffset);
    }

    private static TntHeaderV2 ReadHeader(BinaryReader reader)
    {
        uint version = reader.ReadUInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"Expected TNT V2 version 0x{Version:X4}, got 0x{version:X4}");
        }

        return new TntHeaderV2
        {
            Version = version,
            Width = reader.ReadUInt32(),
            Height = reader.ReadUInt32(),
            SeaLevel = reader.ReadUInt32(),
            HeightMapOffset = reader.ReadUInt32(),
            AttributesOffset = reader.ReadUInt32(),
            FeatureNamesOffset = reader.ReadUInt32(),
            FeatureCount = reader.ReadUInt32(),
            TerrainNamesOffset = reader.ReadUInt32(),
            UMappingOffset = reader.ReadUInt32(),
            VMappingOffset = reader.ReadUInt32(),
            MiniMapOffset = reader.ReadUInt32()
        };
    }

    private static Image CreateHeightmapVisualization(byte[] heightMap, int width, int height)
    {
        var pixelWidth = width * DataUnitSize;
        var pixelHeight = height * DataUnitSize;
        var image = new Image<Rgba32>(pixelWidth, pixelHeight);

        image.ProcessPixelRows(accessor =>
        {
            for (int duY = 0; duY < height; duY++)
            {
                for (int duX = 0; duX < width; duX++)
                {
                    var heightValue = heightMap[duY * width + duX];
                    var color = new Rgba32(heightValue, heightValue, heightValue, 255);

                    // Fill the entire Data Unit (16x16 pixels) with the height color
                    for (int py = 0; py < DataUnitSize; py++)
                    {
                        var row = accessor.GetRowSpan(duY * DataUnitSize + py);
                        for (int px = 0; px < DataUnitSize; px++)
                        {
                            row[duX * DataUnitSize + px] = color;
                        }
                    }
                }
            }
        });

        return image;
    }

    private static Image RenderMapFromTextures(
        List<uint> terrainNames,
        byte[] uMapping,
        byte[] vMapping,
        int guWidth,
        int guHeight,
        Dictionary<string, byte[]> textures)
    {
        var mapWidth = guWidth * GraphicUnitSize;
        var mapHeight = guHeight * GraphicUnitSize;
        var image = new Image<Rgba32>(mapWidth, mapHeight);

        // Cache loaded textures
        var textureCache = new Dictionary<uint, Image<Rgba32>>();

        try
        {
            for (int guY = 0; guY < guHeight; guY++)
            {
                for (int guX = 0; guX < guWidth; guX++)
                {
                    var index = guY * guWidth + guX;
                    var terrainName = terrainNames[index];
                    var u = uMapping[index];
                    var v = vMapping[index];

                    if (!textureCache.TryGetValue(terrainName, out var texture))
                    {
                        texture = LoadTexture(terrainName, textures);
                        if (texture != null)
                        {
                            textureCache[terrainName] = texture;
                        }
                    }

                    if (texture == null)
                    {
                        // Texture not found, default to magenta
                        FillTile(image, guX * GraphicUnitSize, guY * GraphicUnitSize, new Rgba32(255, 0, 255, 255));
                        continue;
                    }

                    // Calculate source position in texture (UV coordinates are in 32-pixel units)
                    var srcX = u * GraphicUnitSize;
                    var srcY = v * GraphicUnitSize;

                    // Copy the 32x32 tile from texture to map
                    CopyTile(texture, srcX, srcY, image, guX * GraphicUnitSize, guY * GraphicUnitSize);
                }
            }
        }
        finally
        {
            foreach (var texture in textureCache.Values)
            {
                texture.Dispose();
            }
        }

        return image;
    }

    private static Image<Rgba32>? LoadTexture(uint terrainName, Dictionary<string, byte[]> textures)
    {
        // Note: We assume all filenames are lowercase
        var filename = TerrainNameToFilename(terrainName).ToLower();

        if (textures.TryGetValue(filename, out var data))
        {
            return Image.Load<Rgba32>(data);
        }

        return null;
    }

    private static void CopyTile(Image<Rgba32> source, int srcX, int srcY, Image<Rgba32> dest, int destX, int destY)
    {
        // Ensure we don't read outside source bounds
        var maxSrcX = Math.Min(srcX + GraphicUnitSize, source.Width);
        var maxSrcY = Math.Min(srcY + GraphicUnitSize, source.Height);

        source.ProcessPixelRows(dest, (srcAccessor, destAccessor) =>
        {
            for (int y = 0; y < GraphicUnitSize && (srcY + y) < maxSrcY; y++)
            {
                var srcRow = srcAccessor.GetRowSpan(srcY + y);
                var destRow = destAccessor.GetRowSpan(destY + y);

                for (int x = 0; x < GraphicUnitSize && (srcX + x) < maxSrcX; x++)
                {
                    destRow[destX + x] = srcRow[srcX + x];
                }
            }
        });
    }

    private static void FillTile(Image<Rgba32> image, int x, int y, Rgba32 color)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int py = 0; py < GraphicUnitSize; py++)
            {
                var row = accessor.GetRowSpan(y + py);
                for (int px = 0; px < GraphicUnitSize; px++)
                {
                    row[x + px] = color;
                }
            }
        });
    }

    private static string TerrainNameToFilename(uint terrainName)
    {
        return $"{terrainName:X8}.JPG";
    }
}