using ii.CompleteDestruction.Model.Hpi;

namespace ii.CompleteDestruction;

public interface IHpiReader
{
    HpiArchive Read(string hpiPath, bool quickRead = true);
    byte[] Extract(string relativePath);
}

