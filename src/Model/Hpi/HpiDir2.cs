using System.Runtime.InteropServices;

namespace ii.CompleteDestruction.Model.Hpi;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HPIDIR2
{
    public int NamePtr;
    public int FirstSubDir;
    public int SubCount;
    public int FirstFile;
    public int FileCount;
}