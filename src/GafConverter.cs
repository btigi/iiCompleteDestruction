using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ii.TotalAnnihilation.Model.Gaf;

namespace ii.TotalAnnihilation;

public class GafConverter
{
    private const byte Transparency = 0;

    public List<Image> Parse(string filePath)
    {
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
                        var image = Image.LoadPixelData<L8>(pixelData, frameData.Width, frameData.Height);
                        result.Add(image);
                    }
                }
            }

            entries.Add(entry);
        }

        return result;
    }
}