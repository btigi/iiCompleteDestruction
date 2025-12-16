using ii.CompleteDestruction.Model.Hpi;
using System.Runtime.InteropServices;

namespace ii.CompleteDestruction;

public class HpiReaderV2 : HpiReaderBase
{
    private byte[] _directory = null!;
    private byte[] _nameBlock = null!;
    private readonly Dictionary<string, HpiEntry2> _fileEntries = new(StringComparer.OrdinalIgnoreCase);

    public override HpiArchive Read(string hpiPath, bool quickRead = true)
    {
        ExtractedFiles.Clear();
        _fileEntries.Clear();
        CurrentArchivePath = hpiPath;

        using var fileStream = new FileStream(hpiPath, FileMode.Open, FileAccess.Read);
        HpiFile = fileStream;

        HpiFile.Seek(8, SeekOrigin.Begin);

        var headerBytes = new byte[Marshal.SizeOf<HpiHeaderV2>()];
        HpiFile.Read(headerBytes, 0, headerBytes.Length);

        var handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
        try
        {
            var header = Marshal.PtrToStructure<HpiHeaderV2>(handle.AddrOfPinnedObject());

            _directory = ReadBlock(header.DirectoryBlock, header.DirectorySize);
            _nameBlock = ReadBlock(header.NameBlock, header.NameSize);

            ProcessDirectory("", 0, quickRead);
        }
        finally
        {
            handle.Free();
        }

        return new HpiArchive
        {
            Files = new List<HpiFileEntry>(ExtractedFiles)
        };
    }

    public override byte[] Extract(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must be provided.", nameof(relativePath));
        }

        if (CurrentArchivePath is null)
        {
            throw new InvalidOperationException("No archive has been read yet.");
        }

        if (!_fileEntries.TryGetValue(relativePath, out var entry))
        {
            throw new FileNotFoundException($"File '{relativePath}' was not found in the archive.", relativePath);
        }

        using (HpiFile = File.Open(CurrentArchivePath, FileMode.Open, FileAccess.Read))
        {
            return ExtractFile(entry);
        }
    }

    private byte[] ReadBlock(int offset, int size)
    {
        var buffer = new byte[size];
        HpiFile.Seek(offset, SeekOrigin.Begin);
        HpiFile.Read(buffer, 0, size);

        if (size >= 4 && BitConverter.ToUInt32(buffer, 0) == SqshMarker)
        {
            var chunk = ByteArrayToStructure<HpiChunk>(buffer, 0);
            var chunkHeaderSize = Marshal.SizeOf<HpiChunk>();
            var compressedData = new byte[chunk.CompressedSize];
            Array.Copy(buffer, chunkHeaderSize, compressedData, 0, chunk.CompressedSize);

            var decompressed = new byte[chunk.DecompressedSize];
            Decompress(decompressed, compressedData, chunk);
            return decompressed;
        }

        return buffer;
    }

    private void ProcessDirectory(string startPath, int offset, bool quickRead)
    {
        var dir = ByteArrayToStructure<HpiDir2>(_directory, offset);

        var dirName = dir.NamePtr > 0 ? ReadStringFromNameBlock(dir.NamePtr) : string.Empty;
        var currentPath = string.IsNullOrEmpty(startPath) ? dirName 
            : string.IsNullOrEmpty(dirName) ? startPath 
            : Path.Combine(startPath, dirName);

        for (var i = 0; i < dir.SubCount; i++)
        {
            var subDirOffset = dir.FirstSubDirectory + i * Marshal.SizeOf<HpiDir2>();
            ProcessDirectory(currentPath, subDirOffset, quickRead);
        }

        for (var i = 0; i < dir.FileCount; i++)
        {
            var fileOffset = dir.FirstFile + i * Marshal.SizeOf<HpiEntry2>();
            var entry = ByteArrayToStructure<HpiEntry2>(_directory, fileOffset);

            var fileName = ReadStringFromNameBlock(entry.NamePtr);
            var relativePath = string.IsNullOrEmpty(currentPath) ? fileName : Path.Combine(currentPath, fileName);

            _fileEntries[relativePath] = entry;

            var fileData = quickRead ? Array.Empty<byte>() : ExtractFile(entry);

            ExtractedFiles.Add(new HpiFileEntry
            {
                RelativePath = relativePath,
                Data = fileData
            });
        }
    }

    private string ReadStringFromNameBlock(int offset)
    {
        return ReadNullTerminatedString(_nameBlock, offset);
    }

    private byte[] ExtractFile(HpiEntry2 entry)
    {
        if (entry.CompressedSize == 0)
        {
            var buffer = new byte[entry.DecompressedSize];
            HpiFile.Seek(entry.Start, SeekOrigin.Begin);
            HpiFile.Read(buffer, 0, entry.DecompressedSize);
            return buffer;
        }

        var compressedBuffer = new byte[entry.CompressedSize];
        HpiFile.Seek(entry.Start, SeekOrigin.Begin);
        HpiFile.Read(compressedBuffer, 0, entry.CompressedSize);

        if (BitConverter.ToUInt32(compressedBuffer, 0) == SqshMarker)
        {
            var chunk = ByteArrayToStructure<HpiChunk>(compressedBuffer, 0);
            var chunkHeaderSize = Marshal.SizeOf<HpiChunk>();
            var compressedData = new byte[chunk.CompressedSize];
            Array.Copy(compressedBuffer, chunkHeaderSize, compressedData, 0, chunk.CompressedSize);

            var decompressed = new byte[entry.DecompressedSize];
            Decompress(decompressed, compressedData, chunk);
            return decompressed;
        }

        return compressedBuffer;
    }
}

