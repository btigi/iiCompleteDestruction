using System.Drawing;

namespace ii.CompleteDestruction.Model.Tnt;

public class TaPalette
{
    private readonly Color[] _palette;

    public TaPalette(byte[] paletteData)
    {
        if (paletteData.Length != 1024) // 256 colors × 4 bytes (RGBA)
            throw new ArgumentException("Palette data must be exactly 1024 bytes (256 RGBA colors)");

        _palette = new Color[256];
        for (int i = 0; i < 256; i++)
        {
            int offset = i * 4;
            byte r = paletteData[offset];
            byte g = paletteData[offset + 1];
            byte b = paletteData[offset + 2];
            // Alpha byte at offset + 3 is ignored in TA
            
            _palette[i] = Color.FromArgb(r, g, b);
        }
    }

    public byte[] ToRgbaBytes(byte[] paletteIndices)
    {
        var rgbaBytes = new byte[paletteIndices.Length * 4];
        for (int i = 0; i < paletteIndices.Length; i++)
        {
            var color = _palette[paletteIndices[i]];
            rgbaBytes[i * 4] = color.R;
            rgbaBytes[i * 4 + 1] = color.G;
            rgbaBytes[i * 4 + 2] = color.B;
            rgbaBytes[i * 4 + 3] = 255; // Full alpha
        }
        return rgbaBytes;
    }

    public byte FindClosestColorIndex(byte r, byte g, byte b)
    {
        int closestIndex = 0;
        int minDistance = int.MaxValue;

        for (int i = 0; i < _palette.Length; i++)
        {
            var color = _palette[i];
            int dr = r - color.R;
            int dg = g - color.G;
            int db = b - color.B;
            int distance = dr * dr + dg * dg + db * db; // Squared Euclidean distance

            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
                
                if (distance == 0)
                    break; // Exact match found
            }
        }

        return (byte)closestIndex;
    }
}