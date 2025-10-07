using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ii.CompleteDestruction.Model.Gaf;

namespace ii.CompleteDestruction;

public partial class GafProcessor
{
    private const byte Transparency = 0;

    public List<GafImageEntry> Read(string filePath, bool processUnsupportedVersions = false)
    {
        var palette = ReadPalette(@"PALETTE.PAL");

        var result = new List<GafImageEntry>();
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));

        var header = new GafHeader
        {
            Version = br.ReadInt32(),
            NumberOfEntries = br.ReadInt32(),
            Padding = br.ReadInt32()
        };

        if (header.Version != 0x00010100 && !processUnsupportedVersions)
        {
            throw new InvalidDataException("Unsupported GAF version.");
        }

        var entryPointers = new int[header.NumberOfEntries];
        for (var i = 0; i < header.NumberOfEntries; i++)
        {
            entryPointers[i] = br.ReadInt32();
        }

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

            var gafImageEntry = new GafImageEntry
            {
                Name = System.Text.Encoding.ASCII.GetString(entry.Name).TrimEnd('\0'),
                Frames = new List<GafFrame>()
            };

            var frameEntries = new List<GafFrameEntry>();
            for (var i = 0; i < entry.NumberOfFrames; i++)
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
                    br.BaseStream.Seek(frameData.OffsetToFrameData, SeekOrigin.Begin);

                    if (frameData.CompressionMethod == 1)
                    {
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
                        for (var i = 0; i < pixelData.Length; i++)
                        {
                            var paletteIndex = pixelData[i];
                            var color = palette[paletteIndex];

                            rgbaData[i * 4] = color.Red;
                            rgbaData[i * 4 + 1] = color.Green;
                            rgbaData[i * 4 + 2] = color.Blue;
                            rgbaData[i * 4 + 3] = 255;
                        }

                        var image = Image.LoadPixelData<Rgba32>(rgbaData, frameData.Width, frameData.Height);
                        gafImageEntry.Frames.Add(new GafFrame
                        {
                            Image = image,
                            XOffset = frameData.XOffset,
                            YOffset = frameData.YOffset,
                            UseCompression = frameData.CompressionMethod == 1
                        });
                    }
                    else
                    {
                        var dataSize = frameData.Width * frameData.Height;
                        var pixelData = new byte[dataSize];

                        var bytesRead = br.Read(pixelData, 0, dataSize);
                        var rgbaData = new byte[frameData.Width * frameData.Height * 4];
                        for (var i = 0; i < pixelData.Length; i++)
                        {
                            var paletteIndex = pixelData[i];
                            var color = palette[paletteIndex];

                            rgbaData[i * 4] = color.Red;
                            rgbaData[i * 4 + 1] = color.Green;
                            rgbaData[i * 4 + 2] = color.Blue;
                            rgbaData[i * 4 + 3] = 255;
                        }

                        var image = Image.LoadPixelData<Rgba32>(rgbaData, frameData.Width, frameData.Height);
                        gafImageEntry.Frames.Add(new GafFrame
                        {
                            Image = image,
                            XOffset = frameData.XOffset,
                            YOffset = frameData.YOffset,
                            UseCompression = frameData.CompressionMethod == 1
                        });
                    }
                }
                else
                {
                    br.BaseStream.Seek(frameData.OffsetToFrameData, SeekOrigin.Begin);
                    
                    var subFramePointers = new int[frameData.NumberOfSubFrames];
                    for (var i = 0; i < frameData.NumberOfSubFrames; i++)
                    {
                        subFramePointers[i] = br.ReadInt32();
                    }
                    
                    foreach (var subFramePointer in subFramePointers)
                    {
                        br.BaseStream.Seek(subFramePointer, SeekOrigin.Begin);
                        
                        var subFrameData = new GafFrameData
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
                        
                        br.BaseStream.Seek(subFrameData.OffsetToFrameData, SeekOrigin.Begin);
                        
                        if (subFrameData.CompressionMethod == 1)
                        {
                            var currentRow = 0;
                            var pixelDataIndex = 0;
                            var pixelData = new byte[subFrameData.Width * subFrameData.Height];
                            while (currentRow < subFrameData.Height)
                            {
                                var bytesThisRow = br.ReadUInt16();
                                var rowBytes = br.ReadBytes(bytesThisRow);
                                var rowBytesIndex = 0;
                                var bytesLeftThisRow = (int)subFrameData.Width;

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

                            var rgbaData = new byte[subFrameData.Width * subFrameData.Height * 4];
                            for (var i = 0; i < pixelData.Length; i++)
                            {
                                var paletteIndex = pixelData[i];
                                var color = palette[paletteIndex];

                                rgbaData[i * 4] = color.Red;
                                rgbaData[i * 4 + 1] = color.Green;
                                rgbaData[i * 4 + 2] = color.Blue;
                                rgbaData[i * 4 + 3] = 255;
                            }

                            var image = Image.LoadPixelData<Rgba32>(rgbaData, subFrameData.Width, subFrameData.Height);
                            gafImageEntry.Frames.Add(new GafFrame
                            {
                                Image = image,
                                XOffset = subFrameData.XOffset,
                                YOffset = subFrameData.YOffset,
                                UseCompression = subFrameData.CompressionMethod == 1
                            });
                        }
                        else
                        {
                            var dataSize = subFrameData.Width * subFrameData.Height;
                            var pixelData = new byte[dataSize];

                            var bytesRead = br.Read(pixelData, 0, dataSize);
                            var rgbaData = new byte[subFrameData.Width * subFrameData.Height * 4];
                            for (var i = 0; i < pixelData.Length; i++)
                            {
                                var paletteIndex = pixelData[i];
                                var color = palette[paletteIndex];

                                rgbaData[i * 4] = color.Red;
                                rgbaData[i * 4 + 1] = color.Green;
                                rgbaData[i * 4 + 2] = color.Blue;
                                rgbaData[i * 4 + 3] = 255;
                            }

                            var image = Image.LoadPixelData<Rgba32>(rgbaData, subFrameData.Width, subFrameData.Height);
                            gafImageEntry.Frames.Add(new GafFrame
                            {
                                Image = image,
                                XOffset = subFrameData.XOffset,
                                YOffset = subFrameData.YOffset,
                                UseCompression = subFrameData.CompressionMethod == 1
                            });
                        }
                    }
                }
            }

            result.Add(gafImageEntry);
        }

        return result;
    }

    public void Write(string filePath, List<GafImageEntry> entries)
    {
        var palette = ReadPalette(@"PALETTE.PAL");

        using var bw = new BinaryWriter(File.Create(filePath));

        bw.Write(0x00010100);
        bw.Write(entries.Count);
        bw.Write(0);

        // Reserve space for entry pointers
        var entryPointerPosition = bw.BaseStream.Position;
        for (var i = 0; i < entries.Count; i++)
        {
            bw.Write(0); // Placeholder
        }

        var entryPointers = new int[entries.Count];

        for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            var entry = entries[entryIndex];
            entryPointers[entryIndex] = (int)bw.BaseStream.Position;

            // Write entry header
            bw.Write((short)entry.Frames.Count); // NumberOfFrames
            bw.Write((short)0); // Unknown1
            bw.Write(0); // Unknown2

            // Write name (32 bytes, null-padded)
            var nameBytes = new byte[32];
            var sourceNameBytes = System.Text.Encoding.ASCII.GetBytes(entry.Name);
            Array.Copy(sourceNameBytes, nameBytes, Math.Min(sourceNameBytes.Length, 31));
            bw.Write(nameBytes);

            // Reserve space for frame entry pointers
            var frameEntryPosition = bw.BaseStream.Position;
            for (var i = 0; i < entry.Frames.Count; i++)
            {
                bw.Write(0); // OffsetToFrameData placeholder
                bw.Write(0); // Unknown
            }

            var framePointers = new int[entry.Frames.Count];

            // Write frame data
            for (var frameIndex = 0; frameIndex < entry.Frames.Count; frameIndex++)
            {
                var frame = entry.Frames[frameIndex];
                framePointers[frameIndex] = (int)bw.BaseStream.Position;

                var width = (short)frame.Image.Width;
                var height = (short)frame.Image.Height;

                // Write frame data header
                bw.Write(width);
                bw.Write(height);
                bw.Write(frame.XOffset);
                bw.Write(frame.YOffset);
                bw.Write((byte)0); // Unknown1
                bw.Write((byte)(frame.UseCompression ? 1 : 0)); // CompressionMethod
                bw.Write((short)0); // NumberOfSubFrames
                bw.Write(0); // Unknown2

                var pixelDataPosition = bw.BaseStream.Position;
                bw.Write(0); // OffsetToFrameData placeholder
                bw.Write(0); // Unknown3

                var pixelDataOffset = (int)bw.BaseStream.Position;

                // Convert image to palette-indexed format
                var pixelData = ImageToPaletteIndexed(frame.Image, palette);

                if (frame.UseCompression)
                {
                    WriteCompressedPixelData(bw, pixelData, width);
                }
                else
                {
                    bw.Write(pixelData);
                }

                // Update OffsetToFrameData
                var currentPosition = bw.BaseStream.Position;
                bw.BaseStream.Seek(pixelDataPosition, SeekOrigin.Begin);
                bw.Write(pixelDataOffset);
                bw.BaseStream.Seek(currentPosition, SeekOrigin.Begin);
            }

            // Update frame entry pointers
            var endPosition = bw.BaseStream.Position;
            bw.BaseStream.Seek(frameEntryPosition, SeekOrigin.Begin);
            for (var i = 0; i < framePointers.Length; i++)
            {
                bw.Write(framePointers[i]);
                bw.Write(0); // Unknown
            }
            bw.BaseStream.Seek(endPosition, SeekOrigin.Begin);
        }

        // Update entry pointers
        bw.BaseStream.Seek(entryPointerPosition, SeekOrigin.Begin);
        foreach (var pointer in entryPointers)
        {
            bw.Write(pointer);
        }
    }

    private byte[] ImageToPaletteIndexed(Image image, Colour[] palette)
    {
        var width = image.Width;
        var height = image.Height;
        var pixelData = new byte[width * height];

        var rgbaImage = image.CloneAs<Rgba32>();

        rgbaImage.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < accessor.Width; x++)
                {
                    var pixel = row[x];
                    var index = FindClosestPaletteIndex(pixel, palette);
                    pixelData[y * width + x] = (byte)index;
                }
            }
        });

        return pixelData;
    }

    private int FindClosestPaletteIndex(Rgba32 pixel, Colour[] palette)
    {
        // If pixel is fully transparent, use transparency index
        if (pixel.A == 0)
        {
            return Transparency;
        }

        var minDistance = int.MaxValue;
        var closestIndex = 0;

        for (var i = 0; i < palette.Length; i++)
        {
            var color = palette[i];
            var dr = pixel.R - color.Red;
            var dg = pixel.G - color.Green;
            var db = pixel.B - color.Blue;
            var distance = dr * dr + dg * dg + db * db;

            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;

                if (distance == 0) 
                    break; // Perfect match
            }
        }

        return closestIndex;
    }

    private void WriteCompressedPixelData(BinaryWriter bw, byte[] pixelData, short width)
    {
        var height = pixelData.Length / width;

        for (var row = 0; row < height; row++)
        {
            var rowStart = row * width;
            var rowData = new List<byte>();

            var col = 0;
            while (col < width)
            {
                var currentPixel = pixelData[rowStart + col];

                // Check for transparency run
                if (currentPixel == Transparency)
                {
                    var runLength = 1;
                    while (col + runLength < width && 
                           pixelData[rowStart + col + runLength] == Transparency && 
                           runLength < 127)
                    {
                        runLength++;
                    }

                    rowData.Add((byte)((runLength << 1) | 0x01));
                    col += runLength;
                }
                // Check for same color run
                else
                {
                    var runLength = 1;
                    while (col + runLength < width && 
                           pixelData[rowStart + col + runLength] == currentPixel && 
                           runLength < 63)
                    {
                        runLength++;
                    }

                    if (runLength >= 3)
                    {
                        rowData.Add((byte)(((runLength - 1) << 2) | 0x02));
                        rowData.Add(currentPixel);
                        col += runLength;
                    }
                    else
                    {
                        // Direct copy
                        var copyLength = 1;
                        while (col + copyLength < width && copyLength < 63)
                        {
                            var nextPixel = pixelData[rowStart + col + copyLength];
                            if (nextPixel == Transparency) break;

                            // Check if it's worth starting a run
                            if (copyLength >= 2)
                            {
                                var ahead = 0;
                                while (col + copyLength + ahead < width && 
                                       pixelData[rowStart + col + copyLength + ahead] == nextPixel &&
                                       ahead < 3)
                                {
                                    ahead++;
                                }
                                if (ahead >= 3) break;
                            }

                            copyLength++;
                        }

                        rowData.Add((byte)((copyLength - 1) << 2));
                        for (var i = 0; i < copyLength; i++)
                        {
                            rowData.Add(pixelData[rowStart + col + i]);
                        }
                        col += copyLength;
                    }
                }
            }

            bw.Write((ushort)rowData.Count);
            bw.Write(rowData.ToArray());
        }
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

        for (var i = 0; i < PaletteSize; i++)
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