using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ii.CompleteDestruction.Model.Gaf;
using ii.CompleteDestruction.Model.Taf;

namespace ii.CompleteDestruction;

// Taf is a copy of Gaf but with different pixel formats (16-bit ARGB 1555 and 4444).
public class TafProcessor
{
    public List<TafImageEntry> Read(string filePath, bool processUnsupportedVersions = false)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br, processUnsupportedVersions);
    }

    public List<TafImageEntry> Read(byte[] data, bool processUnsupportedVersions = false)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br, processUnsupportedVersions);
    }

    private List<TafImageEntry> Read(BinaryReader br, bool processUnsupportedVersions)
    {
        var result = new List<TafImageEntry>();

        var header = new GafHeader
        {
            Version = br.ReadInt32(),
            NumberOfEntries = br.ReadInt32(),
            Padding = br.ReadInt32()
        };

        if (header.Version != 0x00010100 && !processUnsupportedVersions)
        {
            throw new InvalidDataException("Unsupported TAF version.");
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

            var tafImageEntry = new TafImageEntry
            {
                Name = System.Text.Encoding.ASCII.GetString(entry.Name).TrimEnd('\0'),
                Frames = new List<TafFrame>()
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

                if (frameData.NumberOfSubFrames <= 0)
                {
                    br.BaseStream.Seek(frameData.OffsetToFrameData, SeekOrigin.Begin);
                    ReadTafFrame(br, frameData, tafImageEntry);
                }
                else
                {
                    br.BaseStream.Seek(frameData.OffsetToFrameData, SeekOrigin.Begin);

                    var subFrameCount = Math.Max(0, (int)frameData.NumberOfSubFrames);
                    var subFramePointers = new int[subFrameCount];
                    for (var i = 0; i < subFrameCount; i++)
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
                        ReadTafFrame(br, subFrameData, tafImageEntry);
                    }
                }
            }

            result.Add(tafImageEntry);
        }

        return result;
    }

    private void ReadTafFrame(BinaryReader br, GafFrameData frameData, TafImageEntry tafImageEntry)
    {
        var pixelFormat = frameData.CompressionMethod switch
        {
            5 => TafPixelFormat.Argb1555,
            4 => TafPixelFormat.Argb4444,
            _ => throw new InvalidDataException($"Unknown TAF compression method: {frameData.CompressionMethod}")
        };

        // TAF pixel data is raw 16-bit values, no compression
        var pixelCount = frameData.Width * frameData.Height;
        var rgbaData = new byte[pixelCount * 4];

        for (var i = 0; i < pixelCount; i++)
        {
            var pixel = br.ReadUInt16();
            
            byte r, g, b, a;
            if (pixelFormat == TafPixelFormat.Argb1555)
            {
                // 1555 format: ARRRRRGGGGGBBBBB
                // Bit layout: [A:15] [R:14-10] [G:9-5] [B:4-0]
                a = (byte)(((pixel >> 15) & 0x01) * 255);       // 1 bit  -> 0 or 255
                r = (byte)(((pixel >> 10) & 0x1F) * 255 / 31);  // 5 bits -> 0-255
                g = (byte)(((pixel >> 5) & 0x1F) * 255 / 31);   // 5 bits -> 0-255
                b = (byte)((pixel & 0x1F) * 255 / 31);          // 5 bits -> 0-255
            }
            else
            {
                // 4444 format: AAARRRRGGGGBBBB
                // Bit layout: [A:15-12] [R:11-8] [G:7-4] [B:3-0]
                a = (byte)(((pixel >> 12) & 0x0F) * 255 / 15);  // 4 bits -> 0-255
                r = (byte)(((pixel >> 8) & 0x0F) * 255 / 15);   // 4 bits -> 0-255
                g = (byte)(((pixel >> 4) & 0x0F) * 255 / 15);   // 4 bits -> 0-255
                b = (byte)((pixel & 0x0F) * 255 / 15);          // 4 bits -> 0-255
            }

            rgbaData[i * 4] = r;
            rgbaData[i * 4 + 1] = g;
            rgbaData[i * 4 + 2] = b;
            rgbaData[i * 4 + 3] = a;
        }

        var image = Image.LoadPixelData<Rgba32>(rgbaData, frameData.Width, frameData.Height);
        tafImageEntry.Frames.Add(new TafFrame
        {
            Image = image,
            XOffset = frameData.XOffset,
            YOffset = frameData.YOffset,
            PixelFormat = pixelFormat
        });
    }

    public void Write(string filePath, List<TafImageEntry> entries, TafPixelFormat defaultFormat = TafPixelFormat.Argb1555)
    {
        using var bw = new BinaryWriter(File.Create(filePath));

        bw.Write(0x00010100);
        bw.Write(entries.Count);
        bw.Write(0);

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

            bw.Write((short)entry.Frames.Count);
            bw.Write((short)0);
            bw.Write(0);

            var nameBytes = new byte[32];
            var sourceNameBytes = System.Text.Encoding.ASCII.GetBytes(entry.Name);
            Array.Copy(sourceNameBytes, nameBytes, Math.Min(sourceNameBytes.Length, 31));
            bw.Write(nameBytes);

            var frameEntryPosition = bw.BaseStream.Position;
            for (var i = 0; i < entry.Frames.Count; i++)
            {
                bw.Write(0); // OffsetToFrameData placeholder
                bw.Write(0); // Unknown
            }

            var framePointers = new int[entry.Frames.Count];

            for (var frameIndex = 0; frameIndex < entry.Frames.Count; frameIndex++)
            {
                var frame = entry.Frames[frameIndex];
                framePointers[frameIndex] = (int)bw.BaseStream.Position;

                var width = (short)frame.Image.Width;
                var height = (short)frame.Image.Height;
                var pixelFormat = frame.PixelFormat;

                bw.Write(width);
                bw.Write(height);
                bw.Write(frame.XOffset);
                bw.Write(frame.YOffset);
                bw.Write((byte)0); // Unknown1
                bw.Write((byte)pixelFormat);
                bw.Write((short)0); // NumberOfSubFrames
                bw.Write(0); // Unknown2

                var pixelDataPosition = bw.BaseStream.Position;
                bw.Write(0); // OffsetToFrameData placeholder
                bw.Write(0); // Unknown3

                var pixelDataOffset = (int)bw.BaseStream.Position;

                WritePixelData(bw, frame.Image, pixelFormat);

                var currentPosition = bw.BaseStream.Position;
                bw.BaseStream.Seek(pixelDataPosition, SeekOrigin.Begin);
                bw.Write(pixelDataOffset);
                bw.BaseStream.Seek(currentPosition, SeekOrigin.Begin);
            }

            var endPosition = bw.BaseStream.Position;
            bw.BaseStream.Seek(frameEntryPosition, SeekOrigin.Begin);
            for (var i = 0; i < framePointers.Length; i++)
            {
                bw.Write(framePointers[i]);
                bw.Write(0); // Unknown
            }
            bw.BaseStream.Seek(endPosition, SeekOrigin.Begin);
        }

        bw.BaseStream.Seek(entryPointerPosition, SeekOrigin.Begin);
        foreach (var pointer in entryPointers)
        {
            bw.Write(pointer);
        }
    }

    private void WritePixelData(BinaryWriter bw, Image image, TafPixelFormat pixelFormat)
    {
        var rgbaImage = image.CloneAs<Rgba32>();

        rgbaImage.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < accessor.Width; x++)
                {
                    var pixel = row[x];
                    ushort tafPixel;

                    if (pixelFormat == TafPixelFormat.Argb1555)
                    {
                        // 1555 format: ARRRRRGGGGGBBBBB
                        var a = (ushort)(pixel.A >= 128 ? 1 : 0);  // 1 bit alpha (threshold at 128)
                        var r = (ushort)(pixel.R * 31 / 255);      // 5 bits red
                        var g = (ushort)(pixel.G * 31 / 255);      // 5 bits green
                        var b = (ushort)(pixel.B * 31 / 255);      // 5 bits blue
                        tafPixel = (ushort)((a << 15) | (r << 10) | (g << 5) | b);
                    }
                    else
                    {
                        // 4444 format: AAARRRRGGGGBBBB
                        var a = (ushort)(pixel.A * 15 / 255);  // 4 bits alpha
                        var r = (ushort)(pixel.R * 15 / 255);  // 4 bits red
                        var g = (ushort)(pixel.G * 15 / 255);  // 4 bits green
                        var b = (ushort)(pixel.B * 15 / 255);  // 4 bits blue
                        tafPixel = (ushort)((a << 12) | (r << 8) | (g << 4) | b);
                    }

                    bw.Write(tafPixel);
                }
            }
        });
    }
}