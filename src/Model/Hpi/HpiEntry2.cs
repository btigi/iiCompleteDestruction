using System.Runtime.InteropServices;

namespace ii.CompleteDestruction.Model.Hpi;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HPIENTRY2
{
    public int NamePtr;
    public int Start;
    public int DecompressedSize;
    public int CompressedSize;
    public int Date;
    public int Checksum;
}