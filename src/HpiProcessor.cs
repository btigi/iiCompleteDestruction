using ii.CompleteDestruction.Model.Hpi;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ii.CompleteDestruction;

public class HpiProcessor
{
    private const uint Signature = 0x49504148; // HAPI

    private HpiVersion _hpiVersion;
    private int _key;
    private byte[] _directory = null!;
    private FileStream _hpiFile = null!;
    private readonly List<HpiFileEntry> _extractedFiles = [];

    public HpiArchive Read(string hpiPath)
    {
        _extractedFiles.Clear();
        
        using (_hpiFile = File.Open(hpiPath, FileMode.Open, FileAccess.Read))
        {
            ReadVersionHeader();
            
            if (_hpiVersion.HPIMarker == Signature)
            {
                ExtractV1();
            }
            else
            {
                throw new InvalidDataException("Unsupported HPI version");
            }
        }

        return new HpiArchive
        {
            Files = new List<HpiFileEntry>(_extractedFiles)
        };
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

    private void ReadVersionHeader()
    {
        var versionBytes = new byte[Marshal.SizeOf<HpiVersion>()];
        _hpiFile.Read(versionBytes, 0, versionBytes.Length);

        var handle = GCHandle.Alloc(versionBytes, GCHandleType.Pinned);
        try
        {
            _hpiVersion = Marshal.PtrToStructure<HpiVersion>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }

        _hpiFile.Seek(0, SeekOrigin.Begin);
    }

    private int ReadAndDecrypt(int filePosition, byte[] buffer, int bufferSize)
    {
        _hpiFile.Seek(filePosition, SeekOrigin.Begin);
        var bytesRead = _hpiFile.Read(buffer, 0, bufferSize);

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

    private static int LZ77Decompress(byte[] output, byte[] input, HpiChunk chunk)
    {
        var decompressBuffer = new byte[4096];
        var inputIndex = 0;
        var outputIndex = 0;
        var bufferIndex = 1;
        var mask = 1;
        var flags = input[inputIndex++];

        while (true)
        {
            if ((mask & flags) == 0)
            {
                output[outputIndex++] = input[inputIndex];
                decompressBuffer[bufferIndex] = input[inputIndex];
                bufferIndex = (bufferIndex + 1) & 0xFFF;
                inputIndex++;
            }
            else
            {
                var value = BitConverter.ToUInt16(input, inputIndex);
                inputIndex += 2;
                var sourceIndex = value >> 4;
                
                if (sourceIndex == 0)
                {
                    return outputIndex;
                }

                var count = (value & 0x0F) + 2;
                for (var i = 0; i < count; i++)
                {
                    output[outputIndex++] = decompressBuffer[sourceIndex];
                    decompressBuffer[bufferIndex] = decompressBuffer[sourceIndex];
                    sourceIndex = (sourceIndex + 1) & 0xFFF;
                    bufferIndex = (bufferIndex + 1) & 0xFFF;
                }
            }
            
            mask *= 2;
            if (mask > 0xFF)
            {
                mask = 1;
                flags = input[inputIndex++];
            }
        }
    }

    private static int ZLibDecompress(byte[] outData, byte[] inData, HpiChunk chunk)
    {
        // Skip the 2-byte zlib header (CMF and FLG bytes) and 4-byte checksum at the end
        using var compressedStream = new MemoryStream(inData, 2, inData.Length - 6);
        using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        deflateStream.CopyTo(outputStream);
        var decompressedBytes = outputStream.ToArray();
        Array.Copy(decompressedBytes, outData, decompressedBytes.Length);
        return decompressedBytes.Length;
    }

    private int Decompress(byte[] output, byte[] input, HpiChunk chunk)
    {
        var checksum = CalculateChecksum(input, chunk);
        if (chunk.Checksum != checksum)
        {
            throw new InvalidDataException("SQSH checksum error.");
        }

        return chunk.CompMethod switch
        {
            1 => LZ77Decompress(output, input, chunk),
            2 => ZLibDecompress(output, input, chunk),
            _ => 0,
        };
    }

    private static int CalculateChecksum(byte[] input, HpiChunk chunk)
    {
        var checksum = 0;
        for (var i = 0; i < chunk.CompressedSize; i++)
        {
            checksum += input[i];
            if (chunk.Encrypt != 0)
            {
                input[i] = (byte)((uint)input[i] - i ^ i);
            }
        }
        return checksum;
    }

    private byte[] ExtractFile(int offset, int length)
    {
        var chunkCount = (length + 65535) / 65536; // Ceiling division
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

    private void ProcessDirectory(string startPath, int offset)
    {
        var entryCount = BitConverter.ToInt32(_directory, offset);
        var entryListOffset = BitConverter.ToInt32(_directory, offset + 4) - 20;

        for (var i = 0; i < entryCount; i++)
        {
            var currentEntryOffset = entryListOffset + i * Marshal.SizeOf<HpiEntry>();
            var entry = ByteArrayToStructure<HpiEntry>(_directory, currentEntryOffset);

            var entryName = ReadString(_directory, entry.NameOffset - 20);
            var dataOffset = BitConverter.ToInt32(_directory, entry.CountOffset - 20);

            var relativePath = string.IsNullOrEmpty(startPath) ? entryName : Path.Combine(startPath, entryName);

            if (entry.Flag == 1)
            {
                // This is a directory - recurse into it
                ProcessDirectory(relativePath, entry.CountOffset - 20);
            }
            else
            {
                // This is a file - extract it
                var fileLength = BitConverter.ToInt32(_directory, entry.CountOffset - 20 + 4);
                var fileData = ExtractFile(dataOffset, fileLength);
                
                _extractedFiles.Add(new HpiFileEntry
                {
                    RelativePath = relativePath,
                    Data = fileData
                });
            }
        }
    }

    private static string ReadString(byte[] data, int offset)
    {
        var bytes = new List<byte>();
        var index = offset;
        byte currentByte;
        
        while ((currentByte = data[index++]) != 0)
        {
            bytes.Add(currentByte);
        }
        
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static T ByteArrayToStructure<T>(byte[] buffer, int offset) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        if (buffer.Length < size + offset)
        {
            throw new ArgumentException("Buffer is too small to hold the structure.", nameof(buffer));
        }

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(buffer, offset, ptr, size);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private void ExtractV1()
    {
        _hpiFile.Seek(8, SeekOrigin.Begin);

        var headerBytes = new byte[Marshal.SizeOf<HpiHeader>()];
        _hpiFile.Read(headerBytes, 0, headerBytes.Length);
        
        var handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
        try
        {
            var header = Marshal.PtrToStructure<HpiHeader>(handle.AddrOfPinnedObject());
            
            _key = header.Key != 0 ? ~((header.Key * 4) | (header.Key >> 6)) : 0;

            _directory = new byte[header.DirectorySize];
            ReadAndDecrypt(header.Start, _directory, header.DirectorySize - header.Start);
            ProcessDirectory("", 0);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void WriteVersionHeader(BinaryWriter writer)
    {
        writer.Write(Signature); // HAPI marker
        writer.Write(0x00020000); // Version
    }

    private static void WriteMainHeader(BinaryWriter writer, int directoryStart, int directoryEnd)
    {
        var header = new HpiHeader
        {
            DirectorySize = directoryEnd,
            Key = 0, // No encryption
            Start = directoryStart
        };
        
        var bytes = StructureToByteArray(header);
        writer.Write(bytes);
    }

    private static int WriteCompressedFile(BinaryWriter writer, byte[] data)
    {
        var startPosition = (int)writer.BaseStream.Position;
        var chunkCount = (data.Length + 65535) / 65536;
        
        // Write chunk size array
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
        
        // Write chunk sizes
        foreach (var size in chunkSizes)
        {
            writer.Write(size);
        }
        
        // Write compressed chunks
        foreach (var compressedChunk in chunkDataList)
        {
            writer.Write(compressedChunk);
        }
        
        return data.Length; // Return original length
    }

    private static byte[] ZLibCompress(byte[] data)
    {
        using var outputStream = new MemoryStream();
        
        // Write zlib header (CMF and FLG)
        outputStream.WriteByte(0x78); // CMF
        outputStream.WriteByte(0x9C); // FLG
        
        // Compress data using Deflate
        using (var deflateStream = new DeflateStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflateStream.Write(data, 0, data.Length);
        }
        
        // Calculate and write Adler-32 checksum
        var adler = CalculateAdler32(data);
        outputStream.WriteByte((byte)(adler >> 24));
        outputStream.WriteByte((byte)(adler >> 16));
        outputStream.WriteByte((byte)(adler >> 8));
        outputStream.WriteByte((byte)adler);
        
        var compressed = outputStream.ToArray();
        
        // Build HPICHUNK header
        var chunk = new HpiChunk
        {
            Marker = 0x48535153, // SQSH
            Unknown1 = 0x02,
            CompMethod = 2, // ZLib
            Encrypt = 0,
            CompressedSize = compressed.Length - Marshal.SizeOf<HpiChunk>(),
            DecompressedSize = data.Length,
            Checksum = 0
        };
        
        // Calculate checksum
        var checksumData = new byte[compressed.Length - Marshal.SizeOf<HpiChunk>()];
        Array.Copy(compressed, 0, checksumData, 0, checksumData.Length);
        var checksum = 0;
        for (var i = 0; i < checksumData.Length; i++)
        {
            checksum += checksumData[i];
        }
        chunk.Checksum = checksum;
        
        // Build final chunk with header
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
        
        // Build directory tree
        var tree = BuildDirectoryTree(files);
        
        // Calculate directory structure size (without strings)
        var directorySize = CalculateDirectorySize(tree, fileDataMap);
        
        // Pre-calculate string offsets (they'll be at the end)
        var stringOffsets = new Dictionary<string, int>();
        var strings = new List<string>();
        CollectStrings(tree, strings);
        
        var stringPosition = directorySize;
        foreach (var str in strings)
        {
            stringOffsets[str] = stringPosition + 20; // +20 for the offset adjustment
            stringPosition += Encoding.ASCII.GetByteCount(str) + 1; // +1 for null terminator
        }
        
        // Write directory structure with pre-calculated string offsets
        WriteDirectoryNode(writer, tree, stringOffsets, fileDataMap, 20);
        
        // Write all strings at the end
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
        var size = 8; // Entry count (4 bytes) + entries offset (4 bytes)
        
        // Subdirectories
        foreach (var dir in node.Subdirectories)
        {
            size += CalculateDirectorySize(dir.Value, fileDataMap);
        }
        
        // File data blocks (offset + length + flag)
        size += node.Files.Count * 9; // 4 + 4 + 1 bytes per file
        
        // Entry array (nameOffset + countOffset + flag) * entry count
        size += (node.Subdirectories.Count + node.Files.Count) * 9; // 4 + 4 + 1 bytes per entry
        
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
            
            // Navigate/create directory structure
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!currentNode.Subdirectories.ContainsKey(parts[i]))
                {
                    currentNode.Subdirectories[parts[i]] = new DirectoryNode();
                }
                currentNode = currentNode.Subdirectories[parts[i]];
            }
            
            // Add file to current node
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
        
        // Write entry count and placeholder for entries offset
        writer.Write(entries.Count);
        var entriesOffsetPosition = writer.BaseStream.Position;
        writer.Write(0); // Placeholder for entries offset
        
        // Recursively write subdirectories and collect file data
        var subdirectoryOffsets = new Dictionary<string, int>();
        var fileDataOffsets = new Dictionary<string, int>();
        
        foreach (var dir in node.Subdirectories)
        {
            subdirectoryOffsets[dir.Key] = (int)writer.BaseStream.Position + offsetAdjustment;
            WriteDirectoryNode(writer, dir.Value, stringOffsets, fileDataMap, offsetAdjustment);
        }
        
        // Write file data (offset + length + flag) for each file entry
        foreach (var file in node.Files)
        {
            fileDataOffsets[file.fullPath] = (int)writer.BaseStream.Position + offsetAdjustment;
            var (offset, length) = fileDataMap[file.fullPath];
            writer.Write(offset);
            writer.Write(length);
            writer.Write((byte)1); // File flag
        }
        
        // Write entries array
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
        
        // Go back and write entries offset
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
}