using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ii.CompleteDestruction;

public class FntProcessor
{
    private const int GlyphCount = 256;
    private const int MaxDimension = 128;

    public (ushort Height, ushort Flags, Image?[] Glyphs) Read(string filePath)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br);
    }

    public (ushort Height, ushort Flags, Image?[] Glyphs) Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br);
    }

    public void Write(string filePath, ushort height, ushort flags, Image?[] glyphs)
    {
        if (glyphs.Length != GlyphCount)
        {
            throw new ArgumentException($"Expected {GlyphCount} glyphs, got {glyphs.Length}.", nameof(glyphs));
        }

        ValidateHeight(height);

        using var bw = new BinaryWriter(File.Create(filePath));
        Write(bw, height, glyphs, flags);
    }

    private static (ushort Height, ushort Flags, Image?[] Glyphs) Read(BinaryReader br)
    {
        var height = br.ReadUInt16();
        var flags = br.ReadUInt16();

        ValidateHeight(height);

        var offsets = new ushort[GlyphCount];
        for (var i = 0; i < GlyphCount; i++)
        {
            offsets[i] = br.ReadUInt16();
        }

        var fileLength = br.BaseStream.Length;
        var glyphs = new Image?[GlyphCount];

        for (var ch = 0; ch < GlyphCount; ch++)
        {
            var offset = offsets[ch];
            if (offset == 0 || offset >= fileLength)
            {
                continue;
            }

            br.BaseStream.Seek(offset, SeekOrigin.Begin);
            var width = br.ReadByte();
            if (width == 0 || width > MaxDimension)
            {
                continue;
            }

            var totalBits = width * height;
            var totalBytes = (totalBits + 7) / 8;
            if (offset + 1 + totalBytes > fileLength)
            {
                continue;
            }

            var bitData = br.ReadBytes(totalBytes);
            glyphs[ch] = DecodeGlyph(bitData, width, height);
        }

        return (height, flags, glyphs);
    }

    private static void Write(BinaryWriter bw, ushort height, Image?[] glyphs, ushort flags)
    {
        bw.Write(height);
        bw.Write(flags);

        var offsetTablePosition = bw.BaseStream.Position;
        for (var i = 0; i < GlyphCount; i++)
        {
            bw.Write((ushort)0);
        }

        var offsets = new ushort[GlyphCount];
        for (var ch = 0; ch < GlyphCount; ch++)
        {
            var glyph = glyphs[ch];
            if (glyph == null)
            {
                offsets[ch] = 0;
                continue;
            }

            if (glyph.Height != height)
            {
                throw new ArgumentException(
                    $"Glyph {ch} height ({glyph.Height}) does not match font height ({height}).",
                    nameof(glyphs));
            }

            if (glyph.Width <= 0 || glyph.Width > MaxDimension)
            {
                throw new ArgumentException(
                    $"Glyph {ch} width ({glyph.Width}) must be between 1 and {MaxDimension}.",
                    nameof(glyphs));
            }

            offsets[ch] = (ushort)bw.BaseStream.Position;
            bw.Write((byte)glyph.Width);
            bw.Write(EncodeGlyphBits(glyph, glyph.Width, height));
        }

        var endPosition = bw.BaseStream.Position;
        bw.BaseStream.Seek(offsetTablePosition, SeekOrigin.Begin);
        for (var i = 0; i < GlyphCount; i++)
        {
            bw.Write(offsets[i]);
        }

        bw.BaseStream.Seek(endPosition, SeekOrigin.Begin);
    }

    private static Image<Rgba32> DecodeGlyph(byte[] bitData, int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        var bitIndex = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var bytePos = bitIndex / 8;
                    var bitPos = 7 - (bitIndex % 8);
                    var set = bytePos < bitData.Length && ((bitData[bytePos] >> bitPos) & 1) == 1;
                    row[x] = set ? new Rgba32(255, 255, 255, 255) : new Rgba32(0, 0, 0, 0);
                    bitIndex++;
                }
            }
        });

        return image;
    }

    private static byte[] EncodeGlyphBits(Image image, int width, int height)
    {
        var totalBits = width * height;
        var totalBytes = (totalBits + 7) / 8;
        var bitData = new byte[totalBytes];
        var bitIndex = 0;

        ProcessPixels(image, (pixel) =>
        {
            if (IsPixelSet(pixel))
            {
                var bytePos = bitIndex / 8;
                var bitPos = 7 - (bitIndex % 8);
                bitData[bytePos] |= (byte)(1 << bitPos);
            }

            bitIndex++;
        }, width, height);

        return bitData;
    }

    private static void ProcessPixels(Image image, Action<Rgba32> pixelAction, int width, int height)
    {
        if (image is Image<Rgba32> rgba32Image)
        {
            rgba32Image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < width; x++)
                    {
                        pixelAction(row[x]);
                    }
                }
            });
        }
        else
        {
            using var clonedImage = image.CloneAs<Rgba32>();
            clonedImage.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < width; x++)
                    {
                        pixelAction(row[x]);
                    }
                }
            });
        }
    }

    private static bool IsPixelSet(Rgba32 pixel) =>
        pixel.A >= 128 && pixel.R + pixel.G + pixel.B >= 384;

    private static void ValidateHeight(ushort height)
    {
        if (height is < 1 or > MaxDimension)
        {
            throw new InvalidDataException($"Invalid font height: {height}. Expected 1..{MaxDimension}.");
        }
    }
}