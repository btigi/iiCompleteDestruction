using ii.CompleteDestruction.Model.Hpi;
using System.Runtime.InteropServices;

namespace ii.CompleteDestruction;

public class HpiReaderV1 : HpiReaderBase
{
    private const int HeaderOffset = 20; // 8 bytes version + 12 bytes main header

    private int _key;
    private byte[] _directory = null!;
    private readonly Dictionary<string, (int Offset, int Length)> _fileLocations = new(StringComparer.OrdinalIgnoreCase);

    public override HpiArchive Read(string hpiPath, bool quickRead = true)
    {
        ExtractedFiles.Clear();
        _fileLocations.Clear();
        CurrentArchivePath = hpiPath;

        using var fileStream = new FileStream(hpiPath, FileMode.Open, FileAccess.Read);
        HpiFile = fileStream;

        HpiFile.Seek(8, SeekOrigin.Begin);

        var headerBytes = new byte[Marshal.SizeOf<HpiHeader>()];
        HpiFile.Read(headerBytes, 0, headerBytes.Length);

        var handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
        try
        {
            var header = Marshal.PtrToStructure<HpiHeader>(handle.AddrOfPinnedObject());

            _key = header.Key != 0 ? ~((header.Key * 4) | (header.Key >> 6)) : 0;

            _directory = new byte[header.DirectorySize];
            ReadAndDecrypt(header.Start, _directory, header.DirectorySize - header.Start);
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

        if (!_fileLocations.TryGetValue(relativePath, out var location))
        {
            throw new FileNotFoundException($"File '{relativePath}' was not found in the archive.", relativePath);
        }

        using (HpiFile = File.Open(CurrentArchivePath, FileMode.Open, FileAccess.Read))
        {
            return ExtractFile(location.Offset, location.Length);
        }
    }

    private int ReadAndDecrypt(int filePosition, byte[] buffer, int bufferSize)
    {
        HpiFile.Seek(filePosition, SeekOrigin.Begin);
        var bytesRead = HpiFile.Read(buffer, 0, bufferSize);

        if (_key != 0)
        {
            for (var i = 0; i < bufferSize; i++)
            {
                var tempKey = (filePosition + i) ^ _key;
                buffer[i] = (byte)(tempKey ^ ~buffer[i]);
            }
        }

        return bytesRead;
    }

    private byte[] ExtractFile(int offset, int length)
    {
        var chunkCount = (length + 65535) / 65536;
        var chunkSizeArrayLength = chunkCount * sizeof(int);
        var chunkSizes = new int[chunkCount];

        var chunkSizeBytes = new byte[chunkSizeArrayLength];
        ReadAndDecrypt(offset, chunkSizeBytes, chunkSizeArrayLength);
        Buffer.BlockCopy(chunkSizeBytes, 0, chunkSizes, 0, chunkSizeArrayLength);

        offset += chunkSizeArrayLength;

        using var outputStream = new MemoryStream();
        var chunkBuffer = new byte[65536];

        for (var i = 0; i < chunkCount; i++)
        {
            var chunkBytes = new byte[chunkSizes[i]];
            ReadAndDecrypt(offset, chunkBytes, chunkSizes[i]);

            var chunk = ByteArrayToStructure<HpiChunk>(chunkBytes, 0);
            offset += chunkSizes[i];

            var chunkHeaderSize = Marshal.SizeOf<HpiChunk>();
            var compressedData = new byte[chunkBytes.Length - chunkHeaderSize];
            Buffer.BlockCopy(chunkBytes, chunkHeaderSize, compressedData, 0, compressedData.Length);

            var bytesDecompressed = Decompress(chunkBuffer, compressedData, chunk);
            outputStream.Write(chunkBuffer, 0, bytesDecompressed);

            if (bytesDecompressed != chunk.DecompressedSize)
            {
                throw new InvalidDataException($"Decompressed size mismatch: expected {chunk.DecompressedSize}, got {bytesDecompressed}");
            }
        }

        return outputStream.ToArray();
    }

    private void ProcessDirectory(string startPath, int offset, bool quickRead)
    {
        var entryCount = BitConverter.ToInt32(_directory, offset);
        var entryListOffset = BitConverter.ToInt32(_directory, offset + 4) - HeaderOffset;

        for (var i = 0; i < entryCount; i++)
        {
            var currentEntryOffset = entryListOffset + i * Marshal.SizeOf<HpiEntry>();
            var entry = ByteArrayToStructure<HpiEntry>(_directory, currentEntryOffset);

            var entryName = ReadNullTerminatedString(_directory, entry.NameOffset - HeaderOffset);
            var dataOffset = BitConverter.ToInt32(_directory, entry.CountOffset - HeaderOffset);

            var relativePath = string.IsNullOrEmpty(startPath) ? entryName : Path.Combine(startPath, entryName);

            if (entry.Flag == 1)
            {
                ProcessDirectory(relativePath, entry.CountOffset - HeaderOffset, quickRead);
            }
            else
            {
                var fileLength = BitConverter.ToInt32(_directory, entry.CountOffset - HeaderOffset + 4);
                _fileLocations[relativePath] = (dataOffset, fileLength);

                var fileData = quickRead ? Array.Empty<byte>() : ExtractFile(dataOffset, fileLength);

                ExtractedFiles.Add(new HpiFileEntry
                {
                    RelativePath = relativePath,
                    Data = fileData
                });
            }
        }
    }
}

