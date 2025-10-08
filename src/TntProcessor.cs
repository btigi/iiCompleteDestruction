using ii.CompleteDestruction.Model.Tnt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ii.CompleteDestruction;

public partial class TntProcessor
{
    private const int TileWidth = 32;
    private const int TileHeight = 32;

    public const ushort FeatureNone = 0xFFFF;
    public const ushort FeatureVoid = 0xFFFC;
    public const ushort FeatureUnknown = 0xFFFE;

    public TntFile Read(string filePath, TaPalette palette)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));

        var result = new TntFile();
        var header = ReadHeader(br);
        var minimapImage = ProcessMinimap(br, header, palette);
        var mainMapImage = ProcessMainMap(br, header, palette);

        var mapAttributes = new List<MapAttribute>();
        br.BaseStream.Seek(header.MapAttributeOffset, SeekOrigin.Begin);
        for (var i = 0; i < header.TileCount; i++)
        {
            var mapAttribute = new MapAttribute()
            {
                Elevation = br.ReadInt16(),
                TileAnimationIndex = br.ReadUInt16(),
                Unknown = br.ReadInt16()
            };
            mapAttributes.Add(mapAttribute);
        }

        var tileAnimations = new List<TileAnimation>();
        br.BaseStream.Seek(header.TileAnimationOffset, SeekOrigin.Begin);
        for (var i = 0; i < header.TileAnimationCount; i++)
        {
            var tileAnimation = new TileAnimation()
            {
                Index = br.ReadInt32(),
                Name = System.Text.Encoding.ASCII.GetString(br.ReadBytes(128)).TrimEnd('\0'),
            };
            tileAnimations.Add(tileAnimation);
        }

        result.Map = mainMapImage;
        result.Minimap = minimapImage;
        result.SeaLevel = header.SeaLevel;
        result.MapAttributes = mapAttributes;
        result.TileAnimations = tileAnimations;

        return result;
    }

    private TntHeader ReadHeader(BinaryReader reader)
    {
        const int Version = 0x2000;

        uint version = reader.ReadUInt32();
        if (version != Version)
        {
            throw new InvalidDataException("Unsupported TNT version.");
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

    private Image ProcessMinimap(BinaryReader br, TntHeader header, TaPalette palette)
    {
        br.BaseStream.Seek(header.MinimapOffset, SeekOrigin.Begin);

        var width = br.ReadInt32();
        var height = br.ReadInt32();

        var rawData = br.ReadBytes(width * height);
        var actualDimensions = GetMinimapActualSize(rawData, width, height);
        var actualData = CropMinimap(rawData, width, height, actualDimensions.width, actualDimensions.height);

        var mainMapRgba = palette.ToRgbaBytes(actualData);
        return Image.LoadPixelData<Rgba32>(mainMapRgba, actualDimensions.width, actualDimensions.height);
    }

    private Image ProcessMainMap(BinaryReader br, TntHeader header, TaPalette palette)
    {
        var mapWidthInTiles = (int)(header.Width / 2);  // Convert from 16-pixel units to 32-pixel tiles
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

        for (int tileY = 0; tileY < mapHeightInTiles; tileY++)
        {
            for (int tileX = 0; tileX < mapWidthInTiles; tileX++)
            {
                var tileIndex = tileIndices[tileY * mapWidthInTiles + tileX];

                // Ensure tile index is valid
                if (tileIndex < tiles.Length)
                {
                    var tileData = tiles[tileIndex];

                    // Copy tile data to the appropriate position in the map
                    for (int y = 0; y < TileHeight; y++)
                    {
                        for (int x = 0; x < TileWidth; x++)
                        {
                            var srcIndex = y * TileWidth + x;
                            var destX = tileX * TileWidth + x;
                            var destY = tileY * TileHeight + y;
                            var destIndex = destY * mapWidth + destX;

                            if (destIndex < mapBytes.Length)
                            {
                                mapBytes[destIndex] = tileData[srcIndex];
                            }
                        }
                    }
                }
            }
        }

        var mainMapRgba = palette.ToRgbaBytes(mapBytes);
        return Image.LoadPixelData<Rgba32>(mainMapRgba, mapWidth, mapHeight);
    }

    private static (int width, int height) GetMinimapActualSize(byte[] data, int width, int height)
    {
        const byte EndByte = 0x64;

        var actualHeight = 0;
        var actualWidth = 0;

        for (int x = 0; x < width; x++)
        {
            if (data[x] != EndByte)
            {
                actualWidth = x + 1;
            }
        }

        for (int y = 0; y < height; y++)
        {
            if (data[y * width] != EndByte)
            {
                actualHeight = y + 1;
            }
        }

        return (actualWidth, actualHeight);
    }

    private static byte[] CropMinimap(byte[] data, int width, int height, int actualWidth, int actualHeight)
    {
        var result = new byte[actualWidth * actualHeight];

        for (int y = 0; y < actualHeight; y++)
        {
            for (int x = 0; x < actualWidth; x++)
            {
                result[(y * actualWidth) + x] = data[(y * width) + x];
            }
        }

        return result;
    }
}