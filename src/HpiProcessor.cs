using ii.CompleteDestruction.Model.Hpi;
using System.Text;

namespace ii.CompleteDestruction;

public class HpiProcessor : IHpiProcessor
{
    private const string Signature = "HAPI";

    private IHpiProcessor? _processor;

    public HpiArchive Read(string hpiPath, bool quickRead = true)
    {
        _processor = CreateProcessor(hpiPath);
        return _processor.Read(hpiPath, quickRead);
    }

    public byte[] Extract(string relativePath)
    {
        if (_processor is null)
        {
            throw new InvalidOperationException("No archive has been read yet.");
        }
        return _processor.Extract(relativePath);
    }

    public void Write(string outputPath, IEnumerable<HpiFileEntry> files)
    {
        Write(outputPath, files, 1);
    }

    public void Write(string outputPath, IEnumerable<HpiFileEntry> files, int version)
    {
        IHpiProcessor writer = version switch
        {
            1 => new HpiProcessorV1(),
            2 => new HpiProcessorV2(),
            _ => throw new ArgumentException($"Unsupported HPI version: {version}. Use 1 or 2.", nameof(version))
        };
        
        writer.Write(outputPath, files);
    }

    private static IHpiProcessor CreateProcessor(string hpiPath)
    {
        using var stream = File.OpenRead(hpiPath);
        using var reader = new BinaryReader(stream);

        var signatureBytes = reader.ReadBytes(4);
        var fileSignature = Encoding.ASCII.GetString(signatureBytes);
        if (fileSignature != Signature)
        {
            throw new InvalidDataException($"Invalid HPI signature. Expected '{Signature}', got '{fileSignature}'");
        }

        var version = reader.ReadUInt32();
        return version switch
        {
            0x00010000 => new HpiProcessorV1(),
            0x00020000 => new HpiProcessorV2(),
            _ => throw new InvalidDataException($"Unsupported HPI version: 0x{version:X8}")
        };
    }
}
