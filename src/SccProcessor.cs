using System.Text.Json;
using System.Text.Json.Serialization;
using ii.CompleteDestruction.Model.Scc;

namespace ii.CompleteDestruction;

public class SccProcessor
{
    public const uint ExpectedSignature = 0x00011234;
    public static readonly Guid SourceSafeClassId = new("E07D42B1-6888-11D1-98B2-00600895C4B9");

    public SccFile Read(string filePath)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br);
    }

    public SccFile Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br);
    }

    private SccFile Read(BinaryReader br)
    {
        var file = new SccFile();

        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            if (IsZeroBlock(br, 16))
            {
                break;
            }

            var header = ReadHeader(br);
            file.Headers.Add(header);
        }

        return file;
    }

    private static SccFileHeader ReadHeader(BinaryReader br)
    {
        var header = new SccFileHeader
        {
            Signature = br.ReadUInt32(),
            DatabaseId = ReadGuid(br),
            Checksum = br.ReadUInt32(),
            ProjectId = br.ReadUInt32(),
            FileCount = br.ReadUInt32()
        };

        if (header.Signature != ExpectedSignature)
        {
            throw new InvalidDataException(
                $"Invalid SCC signature. Expected 0x{ExpectedSignature:X8}, got 0x{header.Signature:X8}");
        }

        header.Entries = [];
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            if (IsZeroBlock(br, 16) || IsSignatureAtCurrentPosition(br))
            {
                break;
            }

            header.Entries.Add(new SccFileEntry
            {
                FileId = br.ReadUInt32(),
                FileChecksum = br.ReadUInt32(),
                FileTimestamp = br.ReadUInt32(),
                FileVersion = br.ReadUInt32()
            });

            if (header.FileCount > 0 && header.Entries.Count >= header.FileCount)
            {
                break;
            }
        }

        return header;
    }

    public void Write(string filePath, SccFile file)
    {
        using var bw = new BinaryWriter(File.Create(filePath));
        Write(bw, file);
    }

    public byte[] Write(SccFile file)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        Write(bw, file);
        return ms.ToArray();
    }

    private static void Write(BinaryWriter bw, SccFile file)
    {
        foreach (var header in file.Headers)
        {
            bw.Write(header.Signature);
            WriteGuid(bw, header.DatabaseId);
            bw.Write(header.Checksum);
            bw.Write(header.ProjectId);
            bw.Write(header.FileCount);

            foreach (var entry in header.Entries)
            {
                bw.Write(entry.FileId);
                bw.Write(entry.FileChecksum);
                bw.Write(entry.FileTimestamp);
                bw.Write(entry.FileVersion);
            }
        }
    }

    public string ToJson(SccFile file)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        return JsonSerializer.Serialize(file, options);
    }

    public static string EncodeVssObjectName(uint id)
    {
        Span<char> name = stackalloc char[8];
        for (var i = 0; i < 8; i++)
        {
            name[i] = (char)('a' + id % 26);
            id /= 26;
        }

        return new string(name);
    }

    public static uint DecodeVssObjectName(ReadOnlySpan<char> name)
    {
        if (name.Length != 8)
        {
            throw new ArgumentException("VSS object names must be exactly 8 characters.", nameof(name));
        }

        uint id = 0;
        uint multiplier = 1;
        for (var i = 0; i < 8; i++)
        {
            var c = name[i];
            if (c is < 'a' or > 'z')
            {
                throw new ArgumentException($"Invalid VSS object name character '{c}'.", nameof(name));
            }

            id += (uint)(c - 'a') * multiplier;
            multiplier *= 26;
        }

        return id;
    }

    private static bool IsSignatureAtCurrentPosition(BinaryReader br)
    {
        if (br.BaseStream.Length - br.BaseStream.Position < 4)
        {
            return false;
        }

        var position = br.BaseStream.Position;
        var value = br.ReadUInt32();
        br.BaseStream.Position = position;
        return value == ExpectedSignature;
    }

    private static bool IsZeroBlock(BinaryReader br, int length)
    {
        if (br.BaseStream.Length - br.BaseStream.Position < length)
        {
            return false;
        }

        var position = br.BaseStream.Position;
        var bytes = br.ReadBytes(length);
        br.BaseStream.Position = position;
        return bytes.All(b => b == 0);
    }

    private static Guid ReadGuid(BinaryReader br)
    {
        var bytes = br.ReadBytes(16);
        return new Guid(bytes);
    }

    private static void WriteGuid(BinaryWriter bw, Guid guid)
    {
        bw.Write(guid.ToByteArray());
    }
}