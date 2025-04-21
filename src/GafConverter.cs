using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ii.CompleteDestruction.Model.Gaf;

namespace ii.CompleteDestruction;

public class GafConverter
{
    private const byte Transparency = 0;

    public List<Image> Parse(string filePath)
    {
        var palette = ReadPalette(@"PALETTE.PAL");

        var result = new List<Image>();
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));

        var header = new GafHeader
        {
            Version = br.ReadInt32(),
            NumberOfEntries = br.ReadInt32(),
            Padding = br.ReadInt32()
        };

        if (header.Version != 0x00010100)
        {
            throw new InvalidDataException("Unsupported GAF version.");
        }

        var entryPointers = new int[header.NumberOfEntries];
        for (var i = 0; i < header.NumberOfEntries; i++)
        {
            entryPointers[i] = br.ReadInt32();
        }

        var entries = new List<GafEntry>();
        foreach (var pointer in entryPointers)
        {
            br.BaseStream.Seek(pointer, SeekOrigin.Begin);
            var entry = new GafEntry
            {
                NumberOfFrames = br.ReadInt16(),
                Unknown1 = br.ReadInt16(),
                Unknown2 = br.ReadInt32(),
                Name = br.ReadBytes(32)
            };

            var frameEntries = new List<GafFrameEntry>();
            for (int i = 0; i < entry.NumberOfFrames; i++)
            {
                var frameEntry = new GafFrameEntry
                {
                    OffsetToFrameData = br.ReadInt32(),
                    Unknown = br.ReadInt32()
                };
                frameEntries.Add(frameEntry);
            }

            foreach (var frameEntry in frameEntries)
            {
                br.BaseStream.Seek(frameEntry.OffsetToFrameData, SeekOrigin.Begin);
                var frameData = new GafFrameData
                {
                    Width = br.ReadInt16(),
                    Height = br.ReadInt16(),
                    XOffset = br.ReadInt16(),
                    YOffset = br.ReadInt16(),
                    Unknown1 = br.ReadByte(),
                    CompressionMethod = br.ReadByte(),
                    NumberOfSubFrames = br.ReadInt16(),
                    Unknown2 = br.ReadInt32(),
                    OffsetToFrameData = br.ReadInt32(),
                    Unknown3 = br.ReadInt32()
                };

                if (frameData.NumberOfSubFrames == 0)
                {
                    if (frameData.CompressionMethod == 1)
                    {
                        br.BaseStream.Seek(frameData.OffsetToFrameData, SeekOrigin.Begin);
                        var currentRow = 0;
                        var pixelDataIndex = 0;
                        var pixelData = new byte[frameData.Width * frameData.Height];
                        while (currentRow < frameData.Height)
                        {
                            var bytesThisRow = br.ReadUInt16();
                            var rowBytes = br.ReadBytes(bytesThisRow);
                            var rowBytesIndex = 0;
                            var bytesLeftThisRow = (int)frameData.Width;

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

                            // Fill out any remaining pixels in the last row
                            for (var i = 0; i < bytesLeftThisRow; i++)
                            {
                                pixelData[pixelDataIndex] = Transparency;
                                pixelDataIndex++;
                            }

                            currentRow++;
                        }

                        var rgbaData = new byte[frameData.Width * frameData.Height * 4];
                        for (int i = 0; i < pixelData.Length; i++)
                        {
                            var paletteIndex = pixelData[i];
                            var color = palette[paletteIndex];

                            rgbaData[i * 4] = color.Red;
                            rgbaData[i * 4 + 1] = color.Green;
                            rgbaData[i * 4 + 2] = color.Blue;
                            rgbaData[i * 4 + 3] = 255;
                        }

                        var image = Image.LoadPixelData<Rgba32>(rgbaData, frameData.Width, frameData.Height);
                        result.Add(image);
                    }
                    else
                    {
                        var dataSize = frameData.Width * frameData.Height;
                        var pixelData = new byte[dataSize];

                        var bytesRead = br.Read(pixelData, 0, dataSize);
                        var rgbaData = new byte[frameData.Width * frameData.Height * 4];
                        for (int i = 0; i < pixelData.Length; i++)
                        {
                            var paletteIndex = pixelData[i];
                            var color = palette[paletteIndex];

                            rgbaData[i * 4] = color.Red;
                            rgbaData[i * 4 + 1] = color.Green;
                            rgbaData[i * 4 + 2] = color.Blue;
                            rgbaData[i * 4 + 3] = 255;
                        }

                        var image = Image.LoadPixelData<Rgba32>(rgbaData, frameData.Width, frameData.Height);
                        result.Add(image);
                    }
                }
            }

            entries.Add(entry);
        }

        return result;
    }

    private Colour[] ReadPalette(string filePath)
    {
        const int PaletteSize = 256;
        const int ColorSize = 4; // RGBA
        const int ExpectedFileSize = PaletteSize * ColorSize;

        var palette = new Colour[PaletteSize];

        using var br = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read));
        if (br.BaseStream.Length != ExpectedFileSize)
        {
            throw new InvalidDataException($"Palette file must be exactly {ExpectedFileSize} bytes.");
        }

        for (int i = 0; i < PaletteSize; i++)
        {
            var r = br.ReadByte();
            var g = br.ReadByte();
            var b = br.ReadByte();
            var a = br.ReadByte();
            palette[i] = new Colour(r, g, b, a);
        }

        return palette;
    }

    private class Colour
    {
        public Colour(byte red, byte green, byte blue, byte alpha)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        public byte Red { get; set; }
        public byte Green { get; set; }
        public byte Blue { get; set; }
        public byte Alpha { get; set; }
    }
}