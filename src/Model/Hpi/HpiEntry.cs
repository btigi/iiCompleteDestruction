using System.Runtime.InteropServices;

namespace ii.CompleteDestruction.Model.Hpi;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HpiEntry
{
    public int NameOffset;
    public int CountOffset;
    public byte Flag;
}