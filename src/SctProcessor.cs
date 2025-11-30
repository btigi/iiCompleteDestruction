using ii.CompleteDestruction.Model.Sct;
using ii.CompleteDestruction.Model.Tnt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ii.CompleteDestruction;

public class SctProcessor
{
    private const int TileWidth = 32;
    private const int TileHeight = 32;
    private const int MinimapSize = 128;

    public SctFile Read(string filePath, TaPalette palette)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br, palette);
    }

    public SctFile Read(byte[] data, TaPalette palette)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br, palette);
    }

    private SctFile Read(BinaryReader br, TaPalette palette)
    {
        var result = new SctFile();
        var header = ReadHeader(br);
        
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

        // Read height data
        var heightData = new List<HeightData>();
        for (var i = 0; i < header.Width * header.Height * 4; i++)
        {
            heightData.Add(new HeightData
            {
                Height = br.ReadByte(),
                Constant1 = br.ReadInt16(),
                Constant2 = br.ReadByte()
            });
        }

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

    public void Write(string filePath, SctFile sctFile, TaPalette palette)
    {
        using var bw = new BinaryWriter(File.Open(filePath, FileMode.Create));

        // Convert images to palette indices
        var mapBytes = ImageToPaletteIndices(sctFile.Map, palette);
        var minimapBytes = ImageToPaletteIndices(sctFile.Minimap, palette);

        var mapWidthInTiles = sctFile.Map.Width / TileWidth;
        var mapHeightInTiles = sctFile.Map.Height / TileHeight;
        var (tiles, tileIndices) = ExtractTiles(mapBytes, sctFile.Map.Width, sctFile.Map.Height);

        // Calculate offsets
        const int headerSize = 28; // 7 int32 values
        var ptrTiles = headerSize;
        var tilesSize = tiles.Count * TileWidth * TileHeight;
        var ptrData = ptrTiles + tilesSize;
        var sectionDataSize = mapWidthInTiles * mapHeightInTiles * 2; // short values
        var heightDataSize = mapWidthInTiles * mapHeightInTiles * 4 * 4; // 4 HeightData structs per tile, 4 bytes each
        var ptrMinimap = ptrData + sectionDataSize + heightDataSize;

        WriteHeader(bw, new SctHeader
        {
            Version = 2,
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
            if (i < sctFile.HeightInfo.Count)
            {
                var heightData = sctFile.HeightInfo[i];
                bw.Write(heightData.Height);
                bw.Write(heightData.Constant1);
                bw.Write(heightData.Constant2);
            }
            else
            {
                // Default data is we receive insufficient data
                bw.Write((byte)0);
                bw.Write((short)-1);
                bw.Write((byte)0);
            }
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

    private byte[] ImageToPaletteIndices(Image image, TaPalette palette)
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

    private byte FindClosestPaletteIndex(byte r, byte g, byte b, TaPalette palette)
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

    private SctHeader ReadHeader(BinaryReader reader)
    {
        const int ExpectedVersion = 2;

        var version = reader.ReadInt32();
        if (version != ExpectedVersion)
        {
            throw new InvalidDataException($"Unsupported SCT version. Expected {ExpectedVersion}, got {version}.");
        }

        return new SctHeader
        {
            Version = version,
            PtrMinimap = reader.ReadInt32(),
            NumTiles = reader.ReadInt32(),
            PtrTiles = reader.ReadInt32(),
            Width = reader.ReadInt32(),
            Height = reader.ReadInt32(),
            PtrData = reader.ReadInt32()
        };
    }
}