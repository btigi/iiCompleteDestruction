using ii.CompleteDestruction.Model.Hpi;
using System.Runtime.InteropServices;
using System.Text;

namespace ii.CompleteDestruction;

public class HpiProcessorV2 : HpiProcessorBase
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

    public override void Write(string outputPath, IEnumerable<HpiFileEntry> files)
    {
        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream);

        var fileList = files.ToList();
        
        // Write version header
        writer.Write(Encoding.ASCII.GetBytes(Signature));
        writer.Write(0x00020000); // Version 2
        
        // Reserve space for main header (24 bytes)
        var headerPosition = stream.Position;
        stream.Seek(Marshal.SizeOf<HpiHeaderV2>(), SeekOrigin.Current);
        
        var nameBlockData = BuildNameBlock(fileList, out var nameOffsets);
        
        var directoryBlockData = BuildDirectoryBlock(fileList, nameOffsets, out var fileEntryMap);
        
        var fileDataMap = new Dictionary<string, (int start, int compressedSize, int decompressedSize)>();
        foreach (var file in fileList)
        {
            var start = (int)stream.Position;
            var (compressedSize, decompressedSize) = WriteFileData(writer, file.Data);
            fileDataMap[file.RelativePath] = (start, compressedSize, decompressedSize);
        }
        
        UpdateFileEntries(directoryBlockData, fileEntryMap, fileDataMap);
        
        var directoryBlockOffset = (int)stream.Position;
        writer.Write(directoryBlockData);
        
        var nameBlockOffset = (int)stream.Position;
        writer.Write(nameBlockData);
        
        stream.Seek(headerPosition, SeekOrigin.Begin);
        var header = new HpiHeaderV2
        {
            DirectoryBlock = directoryBlockOffset,
            DirectorySize = directoryBlockData.Length,
            NameBlock = nameBlockOffset,
            NameSize = nameBlockData.Length,
            Data = 0x20,
            Last78 = 0
        };
        writer.Write(StructureToByteArray(header));
    }

    private static byte[] BuildNameBlock(List<HpiFileEntry> files, out Dictionary<string, int> nameOffsets)
    {
        nameOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var stream = new MemoryStream();
        
        // Name block starts with a null byte
        stream.WriteByte(0);
        
        var tree = BuildDirectoryTree(files);
        CollectNames(tree, stream, nameOffsets);
        
        return stream.ToArray();
    }

    private static void CollectNames(DirectoryNode node, MemoryStream stream, Dictionary<string, int> nameOffsets)
    {
        foreach (var dir in node.Subdirectories)
        {
            if (!nameOffsets.ContainsKey(dir.Key))
            {
                nameOffsets[dir.Key] = (int)stream.Position;
                var bytes = Encoding.ASCII.GetBytes(dir.Key);
                stream.Write(bytes, 0, bytes.Length);
                stream.WriteByte(0);
            }
            CollectNames(dir.Value, stream, nameOffsets);
        }
        
        foreach (var file in node.Files)
        {
            if (!nameOffsets.ContainsKey(file.name))
            {
                nameOffsets[file.name] = (int)stream.Position;
                var bytes = Encoding.ASCII.GetBytes(file.name);
                stream.Write(bytes, 0, bytes.Length);
                stream.WriteByte(0);
            }
        }
    }

    private static byte[] BuildDirectoryBlock(List<HpiFileEntry> files, Dictionary<string, int> nameOffsets, 
        out Dictionary<string, int> fileEntryMap)
    {
        fileEntryMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        
        var tree = BuildDirectoryTree(files);
        WriteDirectoryNodeV2(writer, tree, string.Empty, nameOffsets, fileEntryMap);
        
        return stream.ToArray();
    }

    private static void WriteDirectoryNodeV2(BinaryWriter writer, DirectoryNode node, string currentPath,
        Dictionary<string, int> nameOffsets, Dictionary<string, int> fileEntryMap)
    {
        var subdirEntries = node.Subdirectories.ToList();
        var fileEntries = node.Files.ToList();
        
        var dirEntryPosition = writer.BaseStream.Position;
        var dirEntry = new HpiDir2
        {
            NamePtr = string.IsNullOrEmpty(currentPath) ? 0 : nameOffsets.GetValueOrDefault(Path.GetFileName(currentPath), 0),
            FirstSubDirectory = 0,
            SubCount = subdirEntries.Count,
            FirstFile = 0,
            FileCount = fileEntries.Count
        };
        writer.Write(StructureToByteArray(dirEntry));
        
        var firstSubDirOffset = (int)writer.BaseStream.Position;
        var subdirPositions = new List<long>();
        foreach (var subdir in subdirEntries)
        {
            subdirPositions.Add(writer.BaseStream.Position);
            var subdirPath = string.IsNullOrEmpty(currentPath) ? subdir.Key : Path.Combine(currentPath, subdir.Key);
            
            var subdirEntry = new HpiDir2
            {
                NamePtr = nameOffsets.GetValueOrDefault(subdir.Key, 0),
                FirstSubDirectory = 0,
                SubCount = subdir.Value.Subdirectories.Count,
                FirstFile = 0,
                FileCount = subdir.Value.Files.Count
            };
            writer.Write(StructureToByteArray(subdirEntry));
        }
        
        var firstFileOffset = (int)writer.BaseStream.Position;
        foreach (var file in fileEntries)
        {
            fileEntryMap[file.fullPath] = (int)writer.BaseStream.Position;
            
            var fileEntry = new HpiEntry2
            {
                NamePtr = nameOffsets.GetValueOrDefault(file.name, 0),
                Start = 0,
                DecompressedSize = 0,
                CompressedSize = 0,
                Date = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Checksum = 0
            };
            writer.Write(StructureToByteArray(fileEntry));
        }
        
        var afterFilesPosition = writer.BaseStream.Position;
        writer.BaseStream.Seek(dirEntryPosition, SeekOrigin.Begin);
        dirEntry.FirstSubDirectory = subdirEntries.Count > 0 ? firstSubDirOffset : 0;
        dirEntry.FirstFile = fileEntries.Count > 0 ? firstFileOffset : 0;
        writer.Write(StructureToByteArray(dirEntry));
        writer.BaseStream.Seek(afterFilesPosition, SeekOrigin.Begin);
        
        for (var i = 0; i < subdirEntries.Count; i++)
        {
            var subdir = subdirEntries[i];
            var subdirPath = string.IsNullOrEmpty(currentPath) ? subdir.Key : Path.Combine(currentPath, subdir.Key);
            
            WriteSubdirectoryChildren(writer, subdir.Value, subdirPath, nameOffsets, fileEntryMap, subdirPositions[i]);
        }
    }

    private static void WriteSubdirectoryChildren(BinaryWriter writer, DirectoryNode node, string currentPath,
        Dictionary<string, int> nameOffsets, Dictionary<string, int> fileEntryMap, long dirEntryPosition)
    {
        var subdirEntries = node.Subdirectories.ToList();
        var fileEntries = node.Files.ToList();
        
        var firstSubDirOffset = (int)writer.BaseStream.Position;
        var subdirPositions = new List<long>();
        foreach (var subdir in subdirEntries)
        {
            subdirPositions.Add(writer.BaseStream.Position);
            
            var subdirEntry = new HpiDir2
            {
                NamePtr = nameOffsets.GetValueOrDefault(subdir.Key, 0),
                FirstSubDirectory = 0,
                SubCount = subdir.Value.Subdirectories.Count,
                FirstFile = 0,
                FileCount = subdir.Value.Files.Count
            };
            writer.Write(StructureToByteArray(subdirEntry));
        }
        
        var firstFileOffset = (int)writer.BaseStream.Position;
        foreach (var file in fileEntries)
        {
            fileEntryMap[file.fullPath] = (int)writer.BaseStream.Position;
            
            var fileEntry = new HpiEntry2
            {
                NamePtr = nameOffsets.GetValueOrDefault(file.name, 0),
                Start = 0,
                DecompressedSize = 0,
                CompressedSize = 0,
                Date = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Checksum = 0
            };
            writer.Write(StructureToByteArray(fileEntry));
        }
        
        var afterFilesPosition = writer.BaseStream.Position;
        writer.BaseStream.Seek(dirEntryPosition, SeekOrigin.Begin);
        
        var dirEntry = new HpiDir2
        {
            NamePtr = nameOffsets.GetValueOrDefault(Path.GetFileName(currentPath), 0),
            FirstSubDirectory = subdirEntries.Count > 0 ? firstSubDirOffset : 0,
            SubCount = subdirEntries.Count,
            FirstFile = fileEntries.Count > 0 ? firstFileOffset : 0,
            FileCount = fileEntries.Count
        };
        writer.Write(StructureToByteArray(dirEntry));
        writer.BaseStream.Seek(afterFilesPosition, SeekOrigin.Begin);
        
        for (var i = 0; i < subdirEntries.Count; i++)
        {
            var subdir = subdirEntries[i];
            var subdirPath = Path.Combine(currentPath, subdir.Key);
            WriteSubdirectoryChildren(writer, subdir.Value, subdirPath, nameOffsets, fileEntryMap, subdirPositions[i]);
        }
    }

    private static (int compressedSize, int decompressedSize) WriteFileData(BinaryWriter writer, byte[] data)
    {
        if (data.Length == 0)
        {
            return (0, 0);
        }
        
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
        
        var chunkHeader = StructureToByteArray(chunk);
        writer.Write(chunkHeader);
        writer.Write(compressed);
        
        return (chunkHeader.Length + compressed.Length, data.Length);
    }

    private static void UpdateFileEntries(byte[] directoryBlock, Dictionary<string, int> fileEntryMap,
        Dictionary<string, (int start, int compressedSize, int decompressedSize)> fileDataMap)
    {
        foreach (var (path, offset) in fileEntryMap)
        {
            if (!fileDataMap.TryGetValue(path, out var fileData))
                continue;
            
            var startBytes = BitConverter.GetBytes(fileData.start);
            Array.Copy(startBytes, 0, directoryBlock, offset + 4, 4);
            
            var decompBytes = BitConverter.GetBytes(fileData.decompressedSize);
            Array.Copy(decompBytes, 0, directoryBlock, offset + 8, 4);
            
            var compBytes = BitConverter.GetBytes(fileData.compressedSize);
            Array.Copy(compBytes, 0, directoryBlock, offset + 12, 4);
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

