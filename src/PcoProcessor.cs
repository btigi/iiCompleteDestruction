using ii.CompleteDestruction.Model.Pco;

namespace ii.CompleteDestruction;

public class PcoProcessor
{
    private const int PaletteEntries = 256;
    private const int PaletteByteCount = PaletteEntries * 3;

    public PcoFile Read(string filePath)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br);
    }

    public PcoFile Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br);
    }

    private PcoFile Read(BinaryReader br)
    {
        var file = new PcoFile
        {
            Signature = br.ReadBytes(4),
            Reserved = br.ReadInt32(),
            Width = br.ReadInt16(),
            Height = br.ReadInt16(),
            CanvasWidth = br.ReadInt16(),
            CanvasHeight = br.ReadInt16()
        };

        // The 256-colour RGB palette occupies the final 768 bytes of the file.
        br.BaseStream.Seek(-PaletteByteCount, SeekOrigin.End);
        var paletteData = br.ReadBytes(PaletteByteCount);

        var palette = new List<(byte R, byte G, byte B)>(PaletteEntries);
        for (var i = 0; i < PaletteEntries; i++)
        {
            var offset = i * 3;
            palette.Add((paletteData[offset], paletteData[offset + 1], paletteData[offset + 2]));
        }

        file.Palette = palette;

        return file;
    }
}