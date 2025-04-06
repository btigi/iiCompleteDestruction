using System.Runtime.InteropServices;

namespace ii.TotalAnnihilation.Model.Hpi;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HPIENTRY
{
    public int NameOffset;
    public int CountOffset;
    public byte Flag;
}