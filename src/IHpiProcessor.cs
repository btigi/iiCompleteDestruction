using ii.CompleteDestruction.Model.Hpi;

namespace ii.CompleteDestruction;

public interface IHpiProcessor
{
    HpiArchive Read(string hpiPath, bool quickRead = true);
    byte[] Extract(string relativePath);
    void Write(string outputPath, IEnumerable<HpiFileEntry> files);
}

