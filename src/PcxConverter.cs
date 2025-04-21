using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ii.CompleteDestruction;

public class PcxConverter
{
    public Image Parse(string filePath)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        var signature = br.ReadByte(); // 0x0A or invalid
        var version = br.ReadByte();
        var encoding = br.ReadByte(); // 1 = RLE
        var bitsPerPixel = br.ReadByte();
        var xMin = br.ReadInt16();
        var yMin = br.ReadInt16();
        var xMax = br.ReadInt16();
        var yMax = br.ReadInt16();
        var horiontalDpi = br.ReadInt16();
        var verticalDpi = br.ReadInt16();
        var egaPalette = br.ReadBytes(48);
        var reserved = br.ReadByte();
        var colourPlanes = br.ReadByte();
        var bytesPerScanLine = br.ReadInt16();
        var paletteType = br.ReadInt16();
        var horizontalSourceResolution = br.ReadInt16();
        var verticalSourceResolution = br.ReadInt16();
        var reserved2 = br.ReadBytes(54);

        if (colourPlanes != 1 || bitsPerPixel != 8)
        {
            throw new Exception("Unsupported PCX format");
        }

        var width = xMax - xMin + 1;
        var height = yMax - yMin + 1;

        var colorPalette = new byte[768];
        var pos = br.BaseStream.Position;
        if (bitsPerPixel == 8 && colourPlanes == 1)
        {
            colorPalette = new byte[768];
            br.BaseStream.Seek(-768, SeekOrigin.End);
            br.BaseStream.Read(colorPalette, 0, 768);
        }
        br.BaseStream.Seek(pos, SeekOrigin.Begin);

        var data = new byte[(width + 1) * 4 * height];

        var stream = br.BaseStream;
        var currentByte = 0;
        var runLength = 0;

        try
        {
            int x, y, i;
            var scanLine = new byte[bytesPerScanLine];
            for (y = 0; y < height; y++)
            {
                // Expand RLE to byte data
                for (i = 0; i < bytesPerScanLine; i++)
                {
                    if (runLength == 0)
                    {
                        currentByte = stream.ReadByte();
                        // 0xC1 to 0xFF indicate the run-length
                        if (currentByte > 191)
                        {
                            runLength = currentByte - 192;
                            currentByte = stream.ReadByte();
                        }
                        else
                        {
                            runLength = 1;
                        }
                    }
                    scanLine[i] = (byte)currentByte;
                    runLength--;
                }

                for (x = 0; x < width; x++)
                {
                    i = scanLine[x];
                    data[4 * (y * width + x)] = colorPalette[i * 3 + 2];
                    data[4 * (y * width + x) + 1] = colorPalette[i * 3 + 1];
                    data[4 * (y * width + x) + 2] = colorPalette[i * 3];
                    data[4 * (y * width + x) + 3] = 0xFF;
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error processing file {ex.Message}");
        }

        var img = Image.LoadPixelData<Bgra32>(data, width, height);
        return img;
    }
}