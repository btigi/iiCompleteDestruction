using ii.CompleteDestruction.Model.Tnt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ii.CompleteDestruction;

public abstract class TntProcessorBase : ITntProcessor
{
    protected const int TileWidth = 32;
    protected const int TileHeight = 32;

    public const ushort FeatureNone = 0xFFFF;
    public const ushort FeatureVoid = 0xFFFC;
    public const ushort FeatureUnknown = 0xFFFE;

    // TA:K specific
    public const ushort TakRoad = 0xFFFB;
    public const ushort TakVoid = 0xFFFF;

    public abstract TntFile Read(string filePath, TaPalette palette);
    public abstract TntFile Read(byte[] data, TaPalette palette);
    public abstract TntFile Read(string filePath, TaPalette palette, Dictionary<string, byte[]> textures);
    public abstract TntFile Read(byte[] data, TaPalette palette, Dictionary<string, byte[]> textures);
    public abstract void Write(string filePath, TntFile tntFile, TaPalette palette);

    protected static Image ProcessMinimapFromRawData(byte[] rawData, int width, int height, TaPalette palette)
    {
        var actualDimensions = GetMinimapActualSize(rawData, width, height);
        var actualData = CropMinimap(rawData, width, height, actualDimensions.width, actualDimensions.height);

        var mainMapRgba = palette.ToRgbaBytes(actualData);
        return Image.LoadPixelData<Rgba32>(mainMapRgba, actualDimensions.width, actualDimensions.height);
    }

    protected static (int width, int height) GetMinimapActualSize(byte[] data, int width, int height)
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

    protected static byte[] CropMinimap(byte[] data, int width, int height, int actualWidth, int actualHeight)
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

    protected static byte[] ImageToPaletteIndices(Image image, TaPalette palette)
    {
        var paletteIndices = new byte[image.Width * image.Height];
        int index = 0;

        if (image is Image<Rgba32> rgba32Image)
        {
            rgba32Image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < pixelRow.Length; x++)
                    {
                        var pixel = pixelRow[x];
                        paletteIndices[index++] = palette.FindClosestColorIndex(pixel.R, pixel.G, pixel.B);
                    }
                }
            });
        }
        else
        {
            using var clonedImage = image.CloneAs<Rgba32>();
            clonedImage.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var pixelRow = accessor.GetRowSpan(y);
                    for (int x = 0; x < pixelRow.Length; x++)
                    {
                        var pixel = pixelRow[x];
                        paletteIndices[index++] = palette.FindClosestColorIndex(pixel.R, pixel.G, pixel.B);
                    }
                }
            });
        }

        return paletteIndices;
    }

    protected static (List<byte[]> tiles, ushort[] tileIndices) ExtractTiles(byte[] mapBytes, int mapWidth, int mapHeight)
    {
        var mapWidthInTiles = mapWidth / TileWidth;
        var mapHeightInTiles = mapHeight / TileHeight;

        var uniqueTiles = new Dictionary<string, int>();
        var tiles = new List<byte[]>();
        var tileIndices = new ushort[mapWidthInTiles * mapHeightInTiles];

        for (int tileY = 0; tileY < mapHeightInTiles; tileY++)
        {
            for (int tileX = 0; tileX < mapWidthInTiles; tileX++)
            {
                var tileData = new byte[TileWidth * TileHeight];

                for (int y = 0; y < TileHeight; y++)
                {
                    for (int x = 0; x < TileWidth; x++)
                    {
                        var srcX = tileX * TileWidth + x;
                        var srcY = tileY * TileHeight + y;
                        var srcIndex = srcY * mapWidth + srcX;
                        var destIndex = y * TileWidth + x;
                        tileData[destIndex] = mapBytes[srcIndex];
                    }
                }

                var tileHash = Convert.ToBase64String(tileData);
                if (!uniqueTiles.TryGetValue(tileHash, out var tileIndex))
                {
                    tileIndex = tiles.Count;
                    tiles.Add(tileData);
                    uniqueTiles[tileHash] = tileIndex;
                }

                tileIndices[tileY * mapWidthInTiles + tileX] = (ushort)tileIndex;
            }
        }

        return (tiles, tileIndices);
    }

    protected static List<MapAttribute> NormalizeAttributes(List<MapAttribute> source, int expectedCount)
    {
        var result = new List<MapAttribute>(expectedCount);
        for (var i = 0; i < expectedCount; i++)
        {
            if (i < source.Count)
            {
                result.Add(source[i]);
            }
            else
            {
                result.Add(new MapAttribute());
            }
        }

        return result;
    }
}