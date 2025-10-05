using System.Runtime.InteropServices;

namespace ii.CompleteDestruction.Model.Hpi;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HpiChunk
{
    public uint Marker;
    public byte Unknown1;
    public byte CompMethod;
    public byte Encrypt;
    public int CompressedSize;
    public int DecompressedSize;
    public int Checksum;
}