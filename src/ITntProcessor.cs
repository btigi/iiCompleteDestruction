using ii.CompleteDestruction.Model.Tnt;

namespace ii.CompleteDestruction;

public interface ITntProcessor
{
    TntFile Read(string filePath, TaPalette palette);
    TntFile Read(byte[] data, TaPalette palette);
    
    TntFile Read(string filePath, TaPalette palette, Dictionary<string, byte[]> textures);
    TntFile Read(byte[] data, TaPalette palette, Dictionary<string, byte[]> textures);
    
    void Write(string filePath, TntFile tntFile, TaPalette palette);
}