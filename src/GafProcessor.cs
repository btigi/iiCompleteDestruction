using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ii.CompleteDestruction.Model.Gaf;

namespace ii.CompleteDestruction;

public class GafProcessor
{
    private const byte Transparency = 0;

    public List<GafImageEntry> Read(string filePath, PalProcessor palProcessor, bool processUnsupportedVersions = false)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br, palProcessor, processUnsupportedVersions);
    }

    public List<GafImageEntry> Read(byte[] data, PalProcessor palProcessor, bool processUnsupportedVersions = false)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br, palProcessor, processUnsupportedVersions);
    }

    private List<GafImageEntry> Read(BinaryReader br, PalProcessor palProcessor, bool processUnsupportedVersions)
    {

        var result = new List<GafImageEntry>();

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

                        var rgbaData = palProcessor.ToRgbaBytes(pixelData);
                        // Set alpha channel based on transparency
                        for (var i = 0; i < pixelData.Length; i++)
                        {
                            if (pixelData[i] == Transparency)
                            {
                                rgbaData[i * 4 + 3] = 0;
                            }
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
                        var rgbaData = palProcessor.ToRgbaBytes(pixelData);
                        // Set alpha channel based on transparency
                        for (var i = 0; i < pixelData.Length; i++)
                        {
                            if (pixelData[i] == Transparency)
                            {
                                rgbaData[i * 4 + 3] = 0;
                            }
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

                            var rgbaData = palProcessor.ToRgbaBytes(pixelData);

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
                            var rgbaData = palProcessor.ToRgbaBytes(pixelData);

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

    public void Write(string filePath, List<GafImageEntry> entries, PalProcessor palProcessor)
    {

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
                var pixelData = ImageToPaletteIndexed(frame.Image, palProcessor);

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

    private byte[] ImageToPaletteIndexed(Image image, PalProcessor palProcessor)
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
                    // If pixel is fully transparent, use transparency index
                    if (pixel.A == 0)
                    {
                        pixelData[y * width + x] = Transparency;
                    }
                    else
                    {
                        pixelData[y * width + x] = palProcessor.FindClosestColorIndex(pixel.R, pixel.G, pixel.B);
                    }
                }
            }
        });

        return pixelData;
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
}