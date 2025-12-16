using ii.CompleteDestruction.Model.Hpi;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ii.CompleteDestruction;

public class HpiProcessor : IHpiReader
{
    private const string Signature = "HAPI";

    private IHpiReader? _reader;

    public HpiArchive Read(string hpiPath, bool quickRead = true)
    {
        _reader = CreateReader(hpiPath);
        return _reader.Read(hpiPath, quickRead);
    }

    public byte[] Extract(string relativePath)
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("No archive has been read yet.");
        }
        return _reader.Extract(relativePath);
    }

    public void Write(string outputPath, IEnumerable<HpiFileEntry> files)
    {
        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream);

        var fileList = files.ToList();
        
        WriteVersionHeader(writer);
        
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
        var directoryData = BuildDirectory(fileList, fileDataMap, directoryStart);
        writer.Write(directoryData);
        var directoryEnd = (int)stream.Position;
        
        stream.Seek(headerPosition, SeekOrigin.Begin);
        WriteMainHeader(writer, directoryStart, directoryEnd);
    }

    private static IHpiReader CreateReader(string hpiPath)
    {
        using var stream = File.OpenRead(hpiPath);
        using var reader = new BinaryReader(stream);

        var signatureBytes = reader.ReadBytes(4);
        var fileSignature = Encoding.ASCII.GetString(signatureBytes);
        if (fileSignature != Signature)
        {
            throw new InvalidDataException($"Invalid HPI signature. Expected '{Signature}', got '{fileSignature}'");
        }

        var version = reader.ReadUInt32();
        return version switch
        {
            0x00010000 => new HpiReaderV1(),
            0x00020000 => new HpiReaderV2(),
            _ => throw new InvalidDataException($"Unsupported HPI version: 0x{version:X8}")
        };
    }

    #region Write

    private static void WriteVersionHeader(BinaryWriter writer)
    {
        var markerBytes = Encoding.ASCII.GetBytes(Signature);
        writer.Write(markerBytes);
        writer.Write(0x00010000); // Version 1
    }

    private static void WriteMainHeader(BinaryWriter writer, int directoryStart, int directoryEnd)
    {
        var header = new HpiHeader
        {
            DirectorySize = directoryEnd,
            Key = 0,
            Start = directoryStart
        };
        
        var bytes = StructureToByteArray(header);
        writer.Write(bytes);
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
            
            var compressed = ZLibCompress(chunkData);
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

    private static byte[] ZLibCompress(byte[] data)
    {
        using var outputStream = new MemoryStream();
        
        outputStream.WriteByte(0x78);
        outputStream.WriteByte(0x9C);
        
        using (var deflateStream = new DeflateStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflateStream.Write(data, 0, data.Length);
        }
        
        var adler = CalculateAdler32(data);
        outputStream.WriteByte((byte)(adler >> 24));
        outputStream.WriteByte((byte)(adler >> 16));
        outputStream.WriteByte((byte)(adler >> 8));
        outputStream.WriteByte((byte)adler);
        
        var compressed = outputStream.ToArray();
        
        var chunk = new HpiChunk
        {
            Marker = 0x48535153,
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

    private static uint CalculateAdler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        
        foreach (var value in data)
        {
            a = (a + value) % mod;
            b = (b + a) % mod;
        }
        
        return (b << 16) | a;
    }

    private static byte[] BuildDirectory(List<HpiFileEntry> files, Dictionary<string, (int offset, int length)> fileDataMap, int directoryStart)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        
        var tree = BuildDirectoryTree(files);
        var directorySize = CalculateDirectorySize(tree, fileDataMap);
        
        var stringOffsets = new Dictionary<string, int>();
        var strings = new List<string>();
        CollectStrings(tree, strings);
        
        var stringPosition = directorySize;
        foreach (var str in strings)
        {
            stringOffsets[str] = stringPosition + 20;
            stringPosition += Encoding.ASCII.GetByteCount(str) + 1;
        }
        
        WriteDirectoryNode(writer, tree, stringOffsets, fileDataMap, 20);
        
        foreach (var str in strings)
        {
            var bytes = Encoding.ASCII.GetBytes(str);
            writer.Write(bytes);
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
    
    private static int CalculateDirectorySize(DirectoryNode node, Dictionary<string, (int offset, int length)> fileDataMap)
    {
        var size = 8;
        
        foreach (var dir in node.Subdirectories)
        {
            size += CalculateDirectorySize(dir.Value, fileDataMap);
        }
        
        size += node.Files.Count * 9;
        size += (node.Subdirectories.Count + node.Files.Count) * 9;
        
        return size;
    }

    private class DirectoryNode
    {
        public Dictionary<string, DirectoryNode> Subdirectories { get; } = new();
        public List<(string name, string fullPath)> Files { get; } = new();
    }

    private static DirectoryNode BuildDirectoryTree(List<HpiFileEntry> files)
    {
        var root = new DirectoryNode();
        
        foreach (var file in files)
        {
            var parts = file.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var currentNode = root;
            
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!currentNode.Subdirectories.ContainsKey(parts[i]))
                {
                    currentNode.Subdirectories[parts[i]] = new DirectoryNode();
                }
                currentNode = currentNode.Subdirectories[parts[i]];
            }
            
            currentNode.Files.Add((parts[^1], file.RelativePath));
        }
        
        return root;
    }

    private static void WriteDirectoryNode(BinaryWriter writer, DirectoryNode node, 
        Dictionary<string, int> stringOffsets, Dictionary<string, (int offset, int length)> fileDataMap, int offsetAdjustment)
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
            subdirectoryOffsets[dir.Key] = (int)writer.BaseStream.Position + offsetAdjustment;
            WriteDirectoryNode(writer, dir.Value, stringOffsets, fileDataMap, offsetAdjustment);
        }
        
        foreach (var file in node.Files)
        {
            fileDataOffsets[file.fullPath] = (int)writer.BaseStream.Position + offsetAdjustment;
            var (offset, length) = fileDataMap[file.fullPath];
            writer.Write(offset);
            writer.Write(length);
            writer.Write((byte)1);
        }
        
        var entriesOffset = (int)writer.BaseStream.Position + offsetAdjustment;
        foreach (var (name, isDirectory, fullPath) in entries)
        {
            var nameOffset = stringOffsets[name];
            int countOffset;
            
            if (isDirectory)
            {
                countOffset = subdirectoryOffsets[name];
            }
            else
            {
                countOffset = fileDataOffsets[fullPath];
            }
            
            writer.Write(nameOffset);
            writer.Write(countOffset);
            writer.Write((byte)(isDirectory ? 1 : 0));
        }
        
        var endPosition = writer.BaseStream.Position;
        writer.BaseStream.Seek(entriesOffsetPosition, SeekOrigin.Begin);
        writer.Write(entriesOffset);
        writer.BaseStream.Seek(endPosition, SeekOrigin.Begin);
    }

    private static byte[] StructureToByteArray<T>(T structure) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var array = new byte[size];
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structure, ptr, false);
            Marshal.Copy(ptr, array, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return array;
    }

    #endregion
}
