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