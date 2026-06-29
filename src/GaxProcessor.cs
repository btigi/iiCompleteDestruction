using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ii.CompleteDestruction.Model.Gax;
using ii.CompleteDestruction.Model.Pco;

namespace ii.CompleteDestruction;

public class GaxProcessor
{
    private const byte Transparency = 0;
    private const int CompressionFlag = 0x100;
    private const int TeamNameLength = 32;

    public List<GaxImage> Read(string filePath, PcoFile pco)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br, pco);
    }

    public List<GaxImage> Read(byte[] data, PcoFile pco)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br, pco);
    }

    private List<GaxImage> Read(BinaryReader br, PcoFile pco)
    {
        var result = new List<GaxImage>();
        var rgbaPalette = BuildRgbaPalette(pco);

        br.ReadBytes(4); // Signature (00 01 01 00)
        var teamCount = br.ReadInt32();
        br.ReadInt32(); // Reserved

        var teamOffsets = new int[teamCount];
        for (var i = 0; i < teamCount; i++)
        {
            teamOffsets[i] = br.ReadInt32();
        }

        foreach (var teamOffset in teamOffsets)
        {
            br.BaseStream.Seek(teamOffset, SeekOrigin.Begin);
            var frameCount = br.ReadInt16();
            br.ReadInt16(); // Version
            br.ReadInt32(); // Reserved
            var teamName = System.Text.Encoding.ASCII.GetString(br.ReadBytes(TeamNameLength)).TrimEnd('\0');

            var recordOffsets = new int[frameCount];
            for (var i = 0; i < frameCount; i++)
            {
                recordOffsets[i] = br.ReadInt32();
                br.ReadInt32(); // Unused, always 10
            }

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var recordOffset = recordOffsets[frameIndex];
                if (recordOffset == 0)
                {
                    continue;
                }

                br.BaseStream.Seek(recordOffset, SeekOrigin.Begin);
                var width = br.ReadInt16();
                var height = br.ReadInt16();
                br.ReadInt32(); // Always 0
                var flags = br.ReadInt32();
                br.ReadInt32(); // Always 0
                var dataOffset = br.ReadInt32();

                if (width <= 0 || height <= 0)
                {
                    continue;
                }

                br.BaseStream.Seek(dataOffset, SeekOrigin.Begin);

                var compressed = (flags & CompressionFlag) == CompressionFlag;
                var pixelData = compressed
                    ? ReadCompressed(br, width, height)
                    : br.ReadBytes(width * height);

                result.Add(new GaxImage
                {
                    Image = CreateImage(pixelData, width, height, rgbaPalette),
                    TeamName = teamName,
                    FrameIndex = frameIndex
                });
            }
        }

        return result;
    }

    private static byte[] ReadCompressed(BinaryReader br, int width, int height)
    {
        var pixelData = new byte[width * height];
        var pixelDataIndex = 0;

        for (var currentRow = 0; currentRow < height; currentRow++)
        {
            var bytesThisRow = br.ReadUInt16();
            var rowBytes = br.ReadBytes(bytesThisRow);
            var rowBytesIndex = 0;
            var bytesLeftThisRow = width;

            while (bytesLeftThisRow > 0 && rowBytesIndex < rowBytes.Length)
            {
                var mask = rowBytes[rowBytesIndex];
                rowBytesIndex++;

                if ((mask & 0x01) == 0x01)
                {
                    // Don't overflow the bytes for this row
                    var count = Math.Min(mask >> 1, bytesLeftThisRow);
                    for (var i = 0; i < count; i++)
                    {
                        pixelData[pixelDataIndex] = Transparency;
                        pixelDataIndex++;
                    }
                    bytesLeftThisRow -= count;
                }
                else if ((mask & 0x02) == 0x02)
                {
                    // Don't overflow the bytes for this row
                    var count = Math.Min((mask >> 2) + 1, bytesLeftThisRow);
                    var paletteIndex = rowBytes[rowBytesIndex];
                    rowBytesIndex++;
                    for (var i = 0; i < count; i++)
                    {
                        pixelData[pixelDataIndex] = paletteIndex;
                        pixelDataIndex++;
                    }
                    bytesLeftThisRow -= count;
                }
                else
                {
                    var inBytes = (mask >> 2) + 1;
                    // Don't overflow the bytes for this row
                    var outBytes = Math.Min(inBytes, bytesLeftThisRow);
                    Array.Copy(rowBytes, rowBytesIndex, pixelData, pixelDataIndex, outBytes);
                    pixelDataIndex += outBytes;
                    rowBytesIndex += outBytes;
                    bytesLeftThisRow -= outBytes;
                }
            }

            // Fill out any remaining pixels in the row
            for (var i = 0; i < bytesLeftThisRow; i++)
            {
                pixelData[pixelDataIndex] = Transparency;
                pixelDataIndex++;
            }
        }

        return pixelData;
    }

    private static Image CreateImage(byte[] pixelData, int width, int height, uint[] rgbaPalette)
    {
        var rgbaData = new byte[pixelData.Length * 4];
        var span = rgbaData.AsSpan();

        for (var i = 0; i < pixelData.Length; i++)
        {
            var paletteIndex = pixelData[i];
            // Index 0 is transparent, everything else is opaque
            var rgba = paletteIndex == Transparency ? 0u : rgbaPalette[paletteIndex];
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(i * 4, 4), rgba);
        }

        return Image.LoadPixelData<Rgba32>(rgbaData, width, height);
    }

    private static uint[] BuildRgbaPalette(PcoFile pco)
    {
        var palette = new uint[256];
        for (var i = 0; i < palette.Length && i < pco.Palette.Count; i++)
        {
            var (r, g, b) = pco.Palette[i];
            palette[i] = (uint)(r | (g << 8) | (b << 16) | (0xFF << 24));
        }

        return palette;
    }
}