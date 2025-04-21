using System.Runtime.InteropServices;

namespace ii.CompleteDestruction.Model.Hpi;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HPIVERSION
{
    public uint HPIMarker;
    public uint Version;
}