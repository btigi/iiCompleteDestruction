using System.Runtime.InteropServices;

namespace ii.TotalAnnihilation.Model;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HPICHUNK
{
    public uint Marker;
    public byte Unknown1;
    public byte CompMethod;
    public byte Encrypt;
    public int CompressedSize;
    public int DecompressedSize;
    public int Checksum;
}