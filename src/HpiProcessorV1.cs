using ii.CompleteDestruction.Model.Hpi;
using System.Runtime.InteropServices;
using System.Text;

namespace ii.CompleteDestruction;

public class HpiProcessorV1 : HpiProcessorBase
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

    public override void Write(string outputPath, IEnumerable<HpiFileEntry> files)
    {
        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream);

        var fileList = files.ToList();
        
        // Write version header
        writer.Write(Encoding.ASCII.GetBytes(Signature));
        writer.Write(0x00010000); // Version 1
        
        var headerPosition = stream.Position;
        stream.Seek(Marshal.SizeOf<HpiHeader>(), SeekOrigin.Current);
        
        var fileDataMap = new Dictionary<string, (int offset, int length)>();
        foreach (var file in fileList)
        {
            var offset = (int)stream.Position;
            var length = WriteCompressedFile(writer, file.Data);
            fileDataMap[file.RelativePath] = (offset, length);
        }
        
        var directoryStart = (int)stream.Position;
        var directoryData = BuildDirectoryV1(fileList, fileDataMap);
        writer.Write(directoryData);
        var directoryEnd = (int)stream.Position;
        
        stream.Seek(headerPosition, SeekOrigin.Begin);
        WriteMainHeader(writer, directoryStart, directoryEnd);
    }

    private static void WriteMainHeader(BinaryWriter writer, int directoryStart, int directoryEnd)
    {
        var header = new HpiHeader
        {
            DirectorySize = directoryEnd,
            Key = 0,
            Start = directoryStart
        };
        writer.Write(StructureToByteArray(header));
    }

    private static int WriteCompressedFile(BinaryWriter writer, byte[] data)
    {
        var chunkCount = (data.Length + 65535) / 65536;
        
        var chunkSizes = new List<int>();
        var chunkDataList = new List<byte[]>();
        
        for (var i = 0; i < chunkCount; i++)
        {
            var offset = i * 65536;
            var length = Math.Min(65536, data.Length - offset);
            var chunkData = new byte[length];
            Array.Copy(data, offset, chunkData, 0, length);
            
            var compressed = CreateSqshChunk(chunkData);
            chunkSizes.Add(compressed.Length);
            chunkDataList.Add(compressed);
        }
        
        foreach (var size in chunkSizes)
        {
            writer.Write(size);
        }
        
        foreach (var compressedChunk in chunkDataList)
        {
            writer.Write(compressedChunk);
        }
        
        return data.Length;
    }

    private static byte[] CreateSqshChunk(byte[] data)
    {
        var compressed = ZLibCompress(data);
        
        var chunk = new HpiChunk
        {
            Marker = SqshMarker,
            Unknown1 = 0x02,
            CompMethod = 2,
            Encrypt = 0,
            CompressedSize = compressed.Length,
            DecompressedSize = data.Length,
            Checksum = 0
        };
        
        var checksum = 0;
        for (var i = 0; i < compressed.Length; i++)
        {
            checksum += compressed[i];
        }
        chunk.Checksum = checksum;
        
        using var finalStream = new MemoryStream();
        using var finalWriter = new BinaryWriter(finalStream);
        finalWriter.Write(StructureToByteArray(chunk));
        finalWriter.Write(compressed);
        
        return finalStream.ToArray();
    }

    private static byte[] BuildDirectoryV1(List<HpiFileEntry> files, Dictionary<string, (int offset, int length)> fileDataMap)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        
        var tree = BuildDirectoryTree(files);
        var directorySize = CalculateDirectorySize(tree);
        
        var stringOffsets = new Dictionary<string, int>();
        var strings = new List<string>();
        CollectStrings(tree, strings);
        
        var stringPosition = directorySize;
        foreach (var str in strings)
        {
            stringOffsets[str] = stringPosition + HeaderOffset;
            stringPosition += Encoding.ASCII.GetByteCount(str) + 1;
        }
        
        WriteDirectoryNode(writer, tree, stringOffsets, fileDataMap);
        
        foreach (var str in strings)
        {
            writer.Write(Encoding.ASCII.GetBytes(str));
            writer.Write((byte)0);
        }
        
        return stream.ToArray();
    }

    private static void CollectStrings(DirectoryNode node, List<string> strings)
    {
        foreach (var dir in node.Subdirectories)
        {
            if (!strings.Contains(dir.Key))
                strings.Add(dir.Key);
            CollectStrings(dir.Value, strings);
        }
        
        foreach (var file in node.Files)
        {
            if (!strings.Contains(file.name))
                strings.Add(file.name);
        }
    }

    private static int CalculateDirectorySize(DirectoryNode node)
    {
        var size = 8; // Entry count + entries offset
        
        foreach (var dir in node.Subdirectories)
        {
            size += CalculateDirectorySize(dir.Value);
        }
        
        size += node.Files.Count * 9; // offset + length + flag per file
        size += (node.Subdirectories.Count + node.Files.Count) * 9; // entry array
        
        return size;
    }

    private static void WriteDirectoryNode(BinaryWriter writer, DirectoryNode node, 
        Dictionary<string, int> stringOffsets, Dictionary<string, (int offset, int length)> fileDataMap)
    {
        var entries = new List<(string name, bool isDirectory, string fullPath)>();
        
        foreach (var dir in node.Subdirectories)
        {
            entries.Add((dir.Key, true, dir.Key));
        }
        
        foreach (var file in node.Files)
        {
            entries.Add((file.name, false, file.fullPath));
        }
        
        writer.Write(entries.Count);
        var entriesOffsetPosition = writer.BaseStream.Position;
        writer.Write(0);
        
        var subdirectoryOffsets = new Dictionary<string, int>();
        var fileDataOffsets = new Dictionary<string, int>();
        
        foreach (var dir in node.Subdirectories)
        {
            subdirectoryOffsets[dir.Key] = (int)writer.BaseStream.Position + HeaderOffset;
            WriteDirectoryNode(writer, dir.Value, stringOffsets, fileDataMap);
        }
        
        foreach (var file in node.Files)
        {
            fileDataOffsets[file.fullPath] = (int)writer.BaseStream.Position + HeaderOffset;
            var (offset, length) = fileDataMap[file.fullPath];
            writer.Write(offset);
            writer.Write(length);
            writer.Write((byte)1);
        }
        
        var entriesOffset = (int)writer.BaseStream.Position + HeaderOffset;
        foreach (var (name, isDirectory, fullPath) in entries)
        {
            var nameOffset = stringOffsets[name];
            var countOffset = isDirectory ? subdirectoryOffsets[name] : fileDataOffsets[fullPath];
            
            writer.Write(nameOffset);
            writer.Write(countOffset);
            writer.Write((byte)(isDirectory ? 1 : 0));
        }
        
        var endPosition = writer.BaseStream.Position;
        writer.BaseStream.Seek(entriesOffsetPosition, SeekOrigin.Begin);
        writer.Write(entriesOffset);
        writer.BaseStream.Seek(endPosition, SeekOrigin.Begin);
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

