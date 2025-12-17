using ii.CompleteDestruction.Model.Hpi;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ii.CompleteDestruction;

public abstract class HpiProcessorBase : IHpiProcessor
{
    protected const string Signature = "HAPI";
    protected const uint SqshMarker = 0x48535153; // 'SQSH'

    protected FileStream HpiFile = null!;
    protected string? CurrentArchivePath;
    protected readonly List<HpiFileEntry> ExtractedFiles = [];

    public abstract HpiArchive Read(string hpiPath, bool quickRead = true);
    public abstract byte[] Extract(string relativePath);
    public abstract void Write(string outputPath, IEnumerable<HpiFileEntry> files);

    protected static int LZ77Decompress(byte[] output, byte[] input, HpiChunk chunk)
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

    protected static int ZLibDecompress(byte[] outData, byte[] inData, HpiChunk chunk)
    {
        using var compressedStream = new MemoryStream(inData, 2, inData.Length - 6);
        using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        deflateStream.CopyTo(outputStream);
        var decompressedBytes = outputStream.ToArray();
        Array.Copy(decompressedBytes, outData, decompressedBytes.Length);
        return decompressedBytes.Length;
    }

    protected int Decompress(byte[] output, byte[] input, HpiChunk chunk)
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

    protected static int CalculateChecksum(byte[] input, HpiChunk chunk)
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

    protected static T ByteArrayToStructure<T>(byte[] buffer, int offset) where T : struct
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

    protected static string ReadNullTerminatedString(byte[] data, int offset)
    {
        var bytes = new List<byte>();
        var index = offset;
        byte currentByte;
        
        while (index < data.Length && (currentByte = data[index++]) != 0)
        {
            bytes.Add(currentByte);
        }
        
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    protected static byte[] ZLibCompress(byte[] data)
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
        
        return outputStream.ToArray();
    }

    protected static uint CalculateAdler32(byte[] data)
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

    protected static byte[] StructureToByteArray<T>(T structure) where T : struct
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

    protected class DirectoryNode
    {
        public Dictionary<string, DirectoryNode> Subdirectories { get; } = new();
        public List<(string name, string fullPath)> Files { get; } = new();
    }

    protected static DirectoryNode BuildDirectoryTree(List<HpiFileEntry> files)
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
}
