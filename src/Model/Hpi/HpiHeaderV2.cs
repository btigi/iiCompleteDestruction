using System.Runtime.InteropServices;

namespace ii.CompleteDestruction.Model.Hpi;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HpiHeaderV2
{
    public int DirectoryBlock;   // Pointer to directory block
    public int DirectorySize;    // Size of directory block
    public int NameBlock;        // Pointer to name block  
    public int NameSize;         // Size of name block
    public int Data;             // Start of file data (usually 0x20)
    public int Last78;           // 0 or pointer to last 78 bytes
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HpiDir2
{
    public int NamePtr;           // Points to name in NameBlock
    public int FirstSubDirectory; // Points to first subdirectory in DirBlock
    public int SubCount;          // Number of subdirectories
    public int FirstFile;         // Points to first file in DirBlock
    public int FileCount;         // Number of files
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HpiEntry2
{
    public int NamePtr;          // Points to name in NameBlock
    public int Start;            // Points to file data in archive
    public int DecompressedSize; // Final decompressed size
    public int CompressedSize;   // Compressed size (0 = not compressed)
    public int Date;             // Date in time_t format
    public int Checksum;         // Checksum
}
