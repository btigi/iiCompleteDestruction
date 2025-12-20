using ii.CompleteDestruction.Model.Tnt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ii.CompleteDestruction;

public class TntProcessorV1 : TntProcessorBase
{
    public const uint Version = 0x2000;

    public override TntFile Read(string filePath, PalProcessor palette)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br, palette);
    }

    public override TntFile Read(byte[] data, PalProcessor palette)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br, palette);
    }

    public override TntFile Read(string filePath, PalProcessor palette, Dictionary<string, byte[]> textures)
    {
        return Read(filePath, palette);
    }

    public override TntFile Read(byte[] data, PalProcessor palette, Dictionary<string, byte[]> textures)
    {
        return Read(data, palette);
    }

    private TntFile Read(BinaryReader br, PalProcessor palette)
    {
        var result = new TntFile();
        var header = ReadHeader(br);
        var minimapImage = ProcessMinimap(br, header, palette);
        var mainMapImage = ProcessMainMap(br, header, palette);

        var mapAttributes = new List<MapAttribute>();
        br.BaseStream.Seek(header.MapAttributeOffset, SeekOrigin.Begin);

        var attributeWidth = (int)header.Width;
        var attributeHeight = (int)header.Height;
        var totalAttributes = attributeWidth * attributeHeight;

        for (var i = 0; i < totalAttributes; i++)
        {
            var mapAttribute = new MapAttribute()
            {
                Elevation = br.ReadByte(),
                TileAnimationIndex = br.ReadUInt16(),
                Unknown = br.ReadByte()
            };
            mapAttributes.Add(mapAttribute);
        }

        var tileAnimations = new List<TileAnimation>();
        br.BaseStream.Seek(header.TileAnimationOffset, SeekOrigin.Begin);
        _ = br.ReadInt32(); // unknown
        for (var i = 0; i < header.TileAnimationCount; i++)
        {
            var name = System.Text.Encoding.ASCII.GetString(br.ReadBytes(128)).Split('\0')[0];
            var index = br.ReadInt32();

            var tileAnimation = new TileAnimation()
            {
                Index = index,
                Name = name
            };
            tileAnimations.Add(tileAnimation);
        }

        result.Map = mainMapImage;
        result.Minimap = minimapImage;
        result.SeaLevel = header.SeaLevel;
        result.AttributeWidth = attributeWidth;
        result.AttributeHeight = attributeHeight;
        result.MapAttributes = mapAttributes;
        result.TileAnimations = tileAnimations;

        return result;
    }

    public override void Write(string filePath, TntFile tntFile, PalProcessor palette)
    {
        using var bw = new BinaryWriter(File.Open(filePath, FileMode.Create));

        var mapBytes = ImageToPaletteIndices(tntFile.Map, palette);
        var minimapBytes = ImageToPaletteIndices(tntFile.Minimap, palette);

        var mapWidthInTiles = tntFile.Map.Width / TileWidth;
        var mapHeightInTiles = tntFile.Map.Height / TileHeight;
        var attributeWidth = tntFile.AttributeWidth > 0 ? tntFile.AttributeWidth : mapWidthInTiles * 2;
        var attributeHeight = tntFile.AttributeHeight > 0 ? tntFile.AttributeHeight : mapHeightInTiles * 2;
        var expectedAttributeCount = Math.Max(1, attributeWidth * attributeHeight);
        tntFile.AttributeWidth = attributeWidth;
        tntFile.AttributeHeight = attributeHeight;
        var normalizedAttributes = NormalizeAttributes(tntFile.MapAttributes, expectedAttributeCount);
        var (tiles, tileIndices) = ExtractTiles(mapBytes, tntFile.Map.Width, tntFile.Map.Height);

        const uint headerSize = 60;
        var mapDataOffset = headerSize;
        var mapDataSize = (uint)(tileIndices.Length * 2);
        var mapAttributeOffset = mapDataOffset + mapDataSize;
        var mapAttributeSize = (uint)(expectedAttributeCount * 4);
        var mapTileOffset = mapAttributeOffset + mapAttributeSize;
        var mapTileSize = (uint)(tiles.Count * TileWidth * TileHeight);
        var tileAnimationOffset = mapTileOffset + mapTileSize;
        var tileAnimationSize = (uint)(4 + tntFile.TileAnimations.Count * 132);
        var minimapOffset = tileAnimationOffset + tileAnimationSize;

        WriteHeader(bw, new TntHeader
        {
            Version = Version,
            Width = (uint)attributeWidth,
            Height = (uint)attributeHeight,
            MapDataOffset = mapDataOffset,
            MapAttributeOffset = mapAttributeOffset,
            MapTileOffset = mapTileOffset,
            TileCount = (uint)tiles.Count,
            TileAnimationCount = (uint)tntFile.TileAnimations.Count,
            TileAnimationOffset = tileAnimationOffset,
            SeaLevel = tntFile.SeaLevel,
            MinimapOffset = minimapOffset,
            Unknown1 = 0,
            Unknown2 = 0,
            Unknown3 = 0,
            Unknown4 = 0,
            Unknown5 = 0
        });

        foreach (var tileIndex in tileIndices)
        {
            bw.Write(tileIndex);
        }

        foreach (var mapAttribute in normalizedAttributes)
        {
            bw.Write(mapAttribute.Elevation);
            bw.Write(mapAttribute.TileAnimationIndex);
            bw.Write(mapAttribute.Unknown);
        }

        foreach (var tile in tiles)
        {
            bw.Write(tile);
        }

        bw.Write(0); // unknown
        foreach (var tileAnimation in tntFile.TileAnimations)
        {
            bw.Write(tileAnimation.Index);
            var nameBytes = new byte[128];
            var sourceNameBytes = System.Text.Encoding.ASCII.GetBytes(tileAnimation.Name);
            Array.Copy(sourceNameBytes, nameBytes, Math.Min(sourceNameBytes.Length, 128));
            bw.Write(nameBytes);
        }

        bw.Write(tntFile.Minimap.Width);
        bw.Write(tntFile.Minimap.Height);

        var paddedMinimapWidth = tntFile.Minimap.Width;
        var paddedMinimapHeight = tntFile.Minimap.Height;
        var paddedMinimap = new byte[paddedMinimapWidth * paddedMinimapHeight];
        Array.Fill(paddedMinimap, (byte)0x64);

        for (int y = 0; y < tntFile.Minimap.Height; y++)
        {
            for (int x = 0; x < tntFile.Minimap.Width; x++)
            {
                paddedMinimap[y * paddedMinimapWidth + x] = minimapBytes[y * tntFile.Minimap.Width + x];
            }
        }

        bw.Write(paddedMinimap);
    }

    private static void WriteHeader(BinaryWriter writer, TntHeader header)
    {
        writer.Write(header.Version);
        writer.Write(header.Width);
        writer.Write(header.Height);
        writer.Write(header.MapDataOffset);
        writer.Write(header.MapAttributeOffset);
        writer.Write(header.MapTileOffset);
        writer.Write(header.TileCount);
        writer.Write(header.TileAnimationCount);
        writer.Write(header.TileAnimationOffset);
        writer.Write(header.SeaLevel);
        writer.Write(header.MinimapOffset);
        writer.Write(header.Unknown1);
        writer.Write(header.Unknown2);
        writer.Write(header.Unknown3);
        writer.Write(header.Unknown4);
        writer.Write(header.Unknown5);
    }

    private static TntHeader ReadHeader(BinaryReader reader)
    {
        uint version = reader.ReadUInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"Expected TNT V1 version 0x{Version:X4}, got 0x{version:X4}");
        }

        return new TntHeader
        {
            Version = version,
            Width = reader.ReadUInt32(),
            Height = reader.ReadUInt32(),
            MapDataOffset = reader.ReadUInt32(),
            MapAttributeOffset = reader.ReadUInt32(),
            MapTileOffset = reader.ReadUInt32(),
            TileCount = reader.ReadUInt32(),
            TileAnimationCount = reader.ReadUInt32(),
            TileAnimationOffset = reader.ReadUInt32(),
            SeaLevel = reader.ReadUInt32(),
            MinimapOffset = reader.ReadUInt32(),
            Unknown1 = reader.ReadUInt32(),
            Unknown2 = reader.ReadUInt32(),
            Unknown3 = reader.ReadUInt32(),
            Unknown4 = reader.ReadUInt32(),
            Unknown5 = reader.ReadUInt32()
        };
    }

    private static Image ProcessMinimap(BinaryReader br, TntHeader header, PalProcessor palette)
    {
        br.BaseStream.Seek(header.MinimapOffset, SeekOrigin.Begin);

        var width = br.ReadInt32();
        var height = br.ReadInt32();

        var rawData = br.ReadBytes(width * height);
        return ProcessMinimapFromRawData(rawData, width, height, palette);
    }

    private static Image ProcessMainMap(BinaryReader br, TntHeader header, PalProcessor palette)
    {
        var mapWidthInTiles = (int)(header.Width / 2);
        var mapHeightInTiles = (int)(header.Height / 2);

        br.BaseStream.Seek(header.MapDataOffset, SeekOrigin.Begin);
        var tileIndices = new ushort[mapWidthInTiles * mapHeightInTiles];
        for (int i = 0; i < tileIndices.Length; i++)
        {
            tileIndices[i] = br.ReadUInt16();
        }

        br.BaseStream.Seek(header.MapTileOffset, SeekOrigin.Begin);
        var tiles = new byte[header.TileCount][];
        for (int i = 0; i < header.TileCount; i++)
        {
            tiles[i] = br.ReadBytes(TileWidth * TileHeight);
        }

        var mapWidth = mapWidthInTiles * TileWidth;
        var mapHeight = mapHeightInTiles * TileHeight;
        var mapBytes = new byte[mapWidth * mapHeight];

        Parallel.For(0, mapHeightInTiles, tileY =>
        {
            var tileRowOffset = tileY * mapWidthInTiles;
            var destBaseY = tileY * TileHeight;

            for (int tileX = 0; tileX < mapWidthInTiles; tileX++)
            {
                var tileIndex = tileIndices[tileRowOffset + tileX];
                if (tileIndex >= tiles.Length)
                {
                    continue;
                }

                var tileData = tiles[tileIndex];
                var destBaseX = tileX * TileWidth;

                for (int y = 0; y < TileHeight; y++)
                {
                    var srcOffset = y * TileWidth;
                    var destRowStart = ((destBaseY + y) * mapWidth) + destBaseX;
                    Buffer.BlockCopy(tileData, srcOffset, mapBytes, destRowStart, TileWidth);
                }
            }
        });

        var mainMapRgba = palette.ToRgbaBytes(mapBytes);
        return Image.LoadPixelData<Rgba32>(mainMapRgba, mapWidth, mapHeight);
    }
}