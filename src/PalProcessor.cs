using System.Buffers.Binary;
using System.Drawing;

namespace ii.CompleteDestruction;

public class PalProcessor
{
    private Color[] _palette = [];
    private uint[] _rgbaPalette = [];
    
    // Linear RGB palette for gamma-correct operations
    private (double R, double G, double B)[] _linearPalette = [];
    // YCbCr palette for perceptually-correct color matching
    private (double Y, double Cb, double Cr)[] _yccPalette = [];

    public void Load(byte[] paletteData)
    {
        if (paletteData.Length != 1024)
            throw new ArgumentException("Palette data must be exactly 1024 bytes (256 RGBA colors)");

        _palette = new Color[256];
        _rgbaPalette = new uint[256];
        _linearPalette = new (double, double, double)[256];
        _yccPalette = new (double, double, double)[256];
        
        for (int i = 0; i < 256; i++)
        {
            int offset = i * 4;
            byte r = paletteData[offset];
            byte g = paletteData[offset + 1];
            byte b = paletteData[offset + 2];
            
            _palette[i] = Color.FromArgb(r, g, b);
            _rgbaPalette[i] = (uint)(r | (g << 8) | (b << 16) | (0xFF << 24));
            
            // Convert to linear RGB
            _linearPalette[i] = (
                SrgbToLinear(r / 255.0),
                SrgbToLinear(g / 255.0),
                SrgbToLinear(b / 255.0)
            );
            
            // Convert to YCbCr for color matching
            _yccPalette[i] = RgbToYcc(_linearPalette[i]);
        }
    }

    // Alpha blending
    public byte[] ToAlp()
    {
        var alp = new byte[256 * 256];
        double halfLight = LightRamp(0.5);

        for (int j = 0; j < 256; j++)
        {
            for (int i = 0; i < 256; i++)
            {
                byte result;
                if (i == 1 || j == 1)
                {
                    // Transparency - preserve destination
                    result = (byte)j;
                }
                else if (j == 0)
                {
                    // Shadow - darken to 50% light
                    var scaled = Scale(_linearPalette[i], halfLight);
                    result = RgbToIndex(scaled);
                }
                else
                {
                    // Blend colors at 50%
                    var blended = (
                        (_linearPalette[i].R + _linearPalette[j].R) * 0.5,
                        (_linearPalette[i].G + _linearPalette[j].G) * 0.5,
                        (_linearPalette[i].B + _linearPalette[j].B) * 0.5
                    );
                    result = RgbToIndex(blended);
                }
                alp[j * 256 + i] = result;
            }
        }

        return alp;
    }
    
    // Shading table
    public byte[] ToShd()
    {
        var shd = new byte[32 * 256];

        for (int j = 0; j < 32; j++)
        {
            double light = LightRamp(j / 14.5);

            for (int i = 0; i < 256; i++)
            {
                var scaled = Scale(_linearPalette[i], light);
                shd[j * 256 + i] = RgbToIndex(scaled);
            }
        }

        return shd;
    }

    // Light table
    public byte[] ToLht()
    {
        var lht = new byte[32 * 256];

        for (int j = 0; j < 32; j++)
        {
            double light = LightRamp((j / 30.0) + 1.0);

            for (int i = 0; i < 256; i++)
            {
                var scaled = Scale(_linearPalette[i], light);
                lht[j * 256 + i] = RgbToIndex(scaled);
            }
        }

        return lht;
    }

    public byte[] ToRgbaBytes(byte[] paletteIndices)
    {
        var rgbaBytes = new byte[paletteIndices.Length * 4];

        if (paletteIndices.Length > 65_000)
        {
            Parallel.For(0, paletteIndices.Length, i =>
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    rgbaBytes.AsSpan(i * 4, 4),
                    _rgbaPalette[paletteIndices[i]]);
            });
        }
        else
        {
            var span = rgbaBytes.AsSpan();
            for (int i = 0; i < paletteIndices.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    span.Slice(i * 4, 4),
                    _rgbaPalette[paletteIndices[i]]);
            }
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
            int distance = dr * dr + dg * dg + db * db;

            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
                
                if (distance == 0)
                    break;
            }
        }

        return (byte)closestIndex;
    }

    #region Color Space Conversions

    private static double Clamp(double x) => Math.Min(Math.Max(0.0, x), 1.0);

    private static double SrgbToLinear(double x)
    {
        if (x <= 0.04045)
            return (x * 25.0) / 323.0;
        else
            return Math.Pow(((x * 200.0) + 11.0) / 211.0, 12.0 / 5.0);
    }

    private static double LinearToBt709(double x)
    {
        if (x >= 0.018)
            return (Math.Pow(x, 0.45) * 1.099) - 0.099;
        else
            return x * 4.500;
    }

    private static double LightRamp(double x)
    {
        const double a = 0.9172891723800194;
        return (SrgbToLinear(x) * a) + (x * (1.0 - a));
    }

    private static (double R, double G, double B) Scale((double R, double G, double B) color, double factor)
    {
        return (color.R * factor, color.G * factor, color.B * factor);
    }

    private static (double R, double G, double B) Clip((double R, double G, double B) color)
    {
        return (Clamp(color.R), Clamp(color.G), Clamp(color.B));
    }

    private static (double Y, double Cb, double Cr) RgbToYcc((double R, double G, double B) linear)
    {
        // Convert linear RGB to gamma-corrected BT.709
        double r = LinearToBt709(linear.R);
        double g = LinearToBt709(linear.G);
        double b = LinearToBt709(linear.B);

        return (
            (r * 2126.0 + g * 7152.0 + b * 722.0) / 10000.0,
            (-r * 2126.0 - g * 7152.0 + b * 9278.0) / 18556.0,
            (r * 7874.0 - g * 7152.0 - b * 722.0) / 15748.0
        );
    }

    private static double DistanceSquared((double Y, double Cb, double Cr) a, (double Y, double Cb, double Cr) b)
    {
        double dy = a.Y - b.Y;
        double dcb = a.Cb - b.Cb;
        double dcr = a.Cr - b.Cr;
        return dy * dy + dcb * dcb + dcr * dcr;
    }

    private byte YccToIndex((double Y, double Cb, double Cr) target)
    {
        int bestIndex = 0;
        double bestError = double.MaxValue;

        // Skip indices 0 and 1 (special meanings)
        for (int i = 2; i < 256; i++)
        {
            double error = DistanceSquared(_yccPalette[i], target);
            if (error < bestError)
            {
                bestIndex = i;
                bestError = error;
            }
        }

        return (byte)bestIndex;
    }

    private byte RgbToIndex((double R, double G, double B) linear)
    {
        // Clip to valid RGB gamut, then convert to YCbCr for matching
        var clipped = Clip(linear);
        var ycc = RgbToYcc(clipped);
        return YccToIndex(ycc);
    }

    #endregion
}
