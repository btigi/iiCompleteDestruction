namespace ii.CompleteDestruction;

public interface ITntProcessor
{
    TntFile Read(string filePath, PalProcessor palette);
    TntFile Read(byte[] data, PalProcessor palette);
    
    TntFile Read(string filePath, PalProcessor palette, Dictionary<string, byte[]> textures);
    TntFile Read(byte[] data, PalProcessor palette, Dictionary<string, byte[]> textures);
    
    void Write(string filePath, TntFile tntFile, PalProcessor palette);
}