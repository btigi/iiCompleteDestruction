using ii.CompleteDestruction.Model.Sct;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ii.CompleteDestruction;

public class SctProcessor
{
    private const int TileWidth = 32;
    private const int TileHeight = 32;
    private const int MinimapSize = 128;
    private const int HeaderSize = 28;
    private const int HeightEntrySizeV3 = 4;
    private const int HeightEntrySizeV2 = 8;
    private const int SupportedVersionV2 = 2;
    private const int SupportedVersionV3 = 3;
    private const int DefaultWriteVersion = SupportedVersionV3;

    public SctFile Read(string filePath, PalProcessor palette)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br, palette);
    }

    public SctFile Read(byte[] data, PalProcessor palette)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br, palette);
    }

    private SctFile Read(BinaryReader br, PalProcessor palette)
    {
        var result = new SctFile();
        var header = ReadHeader(br);
        result.Version = header.Version;

        // Read tiles
        br.BaseStream.Seek(header.PtrTiles, SeekOrigin.Begin);
        var tiles = new byte[header.NumTiles][];
        for (var i = 0; i < header.NumTiles; i++)
        {
            tiles[i] = br.ReadBytes(TileWidth * TileHeight);
        }

        // Read section data (tile indices)
        br.BaseStream.Seek(header.PtrData, SeekOrigin.Begin);
        var tileIndices = new short[header.Width * header.Height];
        for (var i = 0; i < tileIndices.Length; i++)
        {
            tileIndices[i] = br.ReadInt16();
        }

        // Read height data — attribute grid is 2× tile resolution (16 px cells)
        var heightEntryCount = header.Width * 2 * header.Height * 2;
        var heightData = ReadHeightData(br, header.Version, heightEntryCount);

        // Read minimap
        br.BaseStream.Seek(header.PtrMinimap, SeekOrigin.Begin);
        var minimapData = br.ReadBytes(MinimapSize * MinimapSize);

        // Create main map image
        var mapWidth = header.Width * TileWidth;
        var mapHeight = header.Height * TileHeight;
        var mapBytes = new byte[mapWidth * mapHeight];

        for (var tileY = 0; tileY < header.Height; tileY++)
        {
            for (var tileX = 0; tileX < header.Width; tileX++)
            {
                var tileIndex = tileIndices[tileY * header.Width + tileX];

                // Ensure tile index is valid
                if (tileIndex >= 0 && tileIndex < tiles.Length)
                {
                    var tileData = tiles[tileIndex];

                    // Copy tile data to the appropriate position in the map
                    for (var y = 0; y < TileHeight; y++)
                    {
                        for (var x = 0; x < TileWidth; x++)
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
        var minimapRgba = palette.ToRgbaBytes(minimapData);

        result.Map = Image.LoadPixelData<Rgba32>(mainMapRgba, mapWidth, mapHeight);
        result.Minimap = Image.LoadPixelData<Rgba32>(minimapRgba, MinimapSize, MinimapSize);
        result.HeightInfo = heightData;

        return result;
    }

    public void Write(string filePath, SctFile sctFile, PalProcessor palette)
    {
        using var bw = new BinaryWriter(File.Open(filePath, FileMode.Create));

        // Convert images to palette indices
        var mapBytes = ImageToPaletteIndices(sctFile.Map, palette);
        var minimapBytes = ImageToPaletteIndices(sctFile.Minimap, palette);

        var mapWidthInTiles = sctFile.Map.Width / TileWidth;
        var mapHeightInTiles = sctFile.Map.Height / TileHeight;
        var (tiles, tileIndices) = ExtractTiles(mapBytes, sctFile.Map.Width, sctFile.Map.Height);
        var version = sctFile.Version is SupportedVersionV2 or SupportedVersionV3
            ? sctFile.Version
            : DefaultWriteVersion;
        var heightEntrySize = GetHeightEntrySize(version);

        // Calculate offsets
        var ptrTiles = HeaderSize;
        var tilesSize = tiles.Count * TileWidth * TileHeight;
        var ptrData = ptrTiles + tilesSize;
        var sectionDataSize = mapWidthInTiles * mapHeightInTiles * 2; // short values
        var heightDataSize = mapWidthInTiles * mapHeightInTiles * 4 * heightEntrySize;
        var ptrMinimap = ptrData + sectionDataSize + heightDataSize;

        WriteHeader(bw, new SctHeader
        {
            Version = version,
            PtrMinimap = ptrMinimap,
            NumTiles = tiles.Count,
            PtrTiles = ptrTiles,
            Width = mapWidthInTiles,
            Height = mapHeightInTiles,
            PtrData = ptrData
        });

        foreach (var tile in tiles)
        {
            bw.Write(tile);
        }

        foreach (var tileIndex in tileIndices)
        {
            bw.Write(tileIndex);
        }

        var expectedHeightDataCount = mapWidthInTiles * mapHeightInTiles * 4;
        for (var i = 0; i < expectedHeightDataCount; i++)
        {
            var heightData = i < sctFile.HeightInfo.Count
                ? sctFile.HeightInfo[i]
                : new HeightData { Constant1 = -1 };
            WriteHeightData(bw, heightData, version);
        }

        bw.Write(minimapBytes);
    }

    private void WriteHeader(BinaryWriter writer, SctHeader header)
    {
        writer.Write(header.Version);
        writer.Write(header.PtrMinimap);
        writer.Write(header.NumTiles);
        writer.Write(header.PtrTiles);
        writer.Write(header.Width);
        writer.Write(header.Height);
        writer.Write(header.PtrData);
    }

    private byte[] ImageToPaletteIndices(Image image, PalProcessor palette)
    {
        var paletteIndices = new byte[image.Width * image.Height];
        var index = 0;
        
        if (image is Image<Rgba32> rgba32Image)
        {
            rgba32Image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (var x = 0; x < pixelRow.Length; x++)
                    {
                        var pixel = pixelRow[x];
                        paletteIndices[index++] = FindClosestPaletteIndex(pixel.R, pixel.G, pixel.B, palette);
                    }
                }
            });
        }
        else
        {
            using var clonedImage = image.CloneAs<Rgba32>();
            clonedImage.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (var x = 0; x < pixelRow.Length; x++)
                    {
                        var pixel = pixelRow[x];
                        paletteIndices[index++] = FindClosestPaletteIndex(pixel.R, pixel.G, pixel.B, palette);
                    }
                }
            });
        }

        return paletteIndices;
    }

    private byte FindClosestPaletteIndex(byte r, byte g, byte b, PalProcessor palette)
    {
        return palette.FindClosestColorIndex(r, g, b);
    }

    private (List<byte[]> tiles, short[] tileIndices) ExtractTiles(byte[] mapBytes, int mapWidth, int mapHeight)
    {
        var mapWidthInTiles = mapWidth / TileWidth;
        var mapHeightInTiles = mapHeight / TileHeight;
        
        var uniqueTiles = new Dictionary<string, int>();
        var tiles = new List<byte[]>();
        var tileIndices = new short[mapWidthInTiles * mapHeightInTiles];

        for (var tileY = 0; tileY < mapHeightInTiles; tileY++)
        {
            for (var tileX = 0; tileX < mapWidthInTiles; tileX++)
            {
                var tileData = new byte[TileWidth * TileHeight];
                
                // Extract tile data from map
                for (var y = 0; y < TileHeight; y++)
                {
                    for (var x = 0; x < TileWidth; x++)
                    {
                        var srcX = tileX * TileWidth + x;
                        var srcY = tileY * TileHeight + y;
                        var srcIndex = srcY * mapWidth + srcX;
                        var destIndex = y * TileWidth + x;
                        tileData[destIndex] = mapBytes[srcIndex];
                    }
                }

                // Check if this tile already exists
                var tileHash = Convert.ToBase64String(tileData);
                if (!uniqueTiles.TryGetValue(tileHash, out var tileIndex))
                {
                    tileIndex = tiles.Count;
                    tiles.Add(tileData);
                    uniqueTiles[tileHash] = tileIndex;
                }

                tileIndices[tileY * mapWidthInTiles + tileX] = (short)tileIndex;
            }
        }

        return (tiles, tileIndices);
    }

    private static int GetHeightEntrySize(int version) =>
        version == SupportedVersionV2 ? HeightEntrySizeV2 : HeightEntrySizeV3;

    private static List<HeightData> ReadHeightData(BinaryReader br, int version, int count)
    {
        var entrySize = GetHeightEntrySize(version);
        var heightData = new List<HeightData>(count);

        for (var i = 0; i < count; i++)
        {
            var entry = br.ReadBytes(entrySize);
            if (entry.Length < HeightEntrySizeV3)
            {
                break;
            }

            var data = new HeightData
            {
                Height = entry[0],
                Constant1 = BitConverter.ToInt16(entry, 1),
                Constant2 = entry[3]
            };

            if (entrySize == HeightEntrySizeV2 && entry.Length >= HeightEntrySizeV2)
            {
                data.Reserved = BitConverter.ToUInt32(entry, 4);
            }

            heightData.Add(data);
        }

        return heightData;
    }

    private static void WriteHeightData(BinaryWriter bw, HeightData heightData, int version)
    {
        bw.Write(heightData.Height);
        bw.Write(heightData.Constant1);
        bw.Write(heightData.Constant2);

        if (version == SupportedVersionV2)
        {
            bw.Write(heightData.Reserved);
        }
    }

    private SctHeader ReadHeader(BinaryReader reader)
    {
        var fileLength = reader.BaseStream.Length;
        var version = reader.ReadInt32();
        if (version != SupportedVersionV2 && version != SupportedVersionV3)
        {
            throw new InvalidDataException(
                $"Unsupported SCT version. Expected {SupportedVersionV2} or {SupportedVersionV3}, got {version}.");
        }

        var header = new SctHeader
        {
            Version = version,
            PtrMinimap = reader.ReadInt32(),
            NumTiles = reader.ReadInt32(),
            PtrTiles = reader.ReadInt32(),
            Width = reader.ReadInt32(),
            Height = reader.ReadInt32(),
            PtrData = reader.ReadInt32()
        };

        ValidateHeader(header, fileLength);
        return header;
    }

    private static void ValidateHeader(SctHeader header, long fileLength)
    {
        if (header.NumTiles < 0 || header.Width <= 0 || header.Height <= 0)
        {
            throw new InvalidDataException("SCT header contains invalid dimensions or tile count.");
        }

        if (header.PtrTiles < HeaderSize || header.PtrTiles >= fileLength)
        {
            throw new InvalidDataException($"SCT PtrTiles ({header.PtrTiles}) is outside the file.");
        }

        if (header.PtrData < HeaderSize || header.PtrData >= fileLength)
        {
            throw new InvalidDataException($"SCT PtrData ({header.PtrData}) is outside the file.");
        }

        if (header.PtrMinimap > 0 && (header.PtrMinimap < HeaderSize || header.PtrMinimap >= fileLength))
        {
            throw new InvalidDataException($"SCT PtrMinimap ({header.PtrMinimap}) is outside the file.");
        }

        var tilesEnd = (long)header.PtrTiles + header.NumTiles * TileWidth * TileHeight;
        if (tilesEnd > fileLength)
        {
            throw new InvalidDataException("SCT tile block extends past the end of the file.");
        }

        var tileMapBytes = (long)header.Width * header.Height * 2;
        var heightBytes = (long)header.Width * 2 * header.Height * 2 * GetHeightEntrySize(header.Version);
        var dataEnd = (long)header.PtrData + tileMapBytes + heightBytes;
        if (dataEnd > fileLength)
        {
            throw new InvalidDataException("SCT section data extends past the end of the file.");
        }

        if (header.PtrMinimap > 0)
        {
            var minimapEnd = (long)header.PtrMinimap + MinimapSize * MinimapSize;
            if (minimapEnd > fileLength)
            {
                throw new InvalidDataException("SCT minimap extends past the end of the file.");
            }
        }
    }
}