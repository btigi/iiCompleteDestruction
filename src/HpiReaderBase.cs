using ii.CompleteDestruction.Model.Hpi;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ii.CompleteDestruction;

public abstract class HpiReaderBase : IHpiReader
{
    protected const string Signature = "HAPI";
    protected const uint SqshMarker = 0x48535153; // 'SQSH'

    protected FileStream HpiFile = null!;
    protected string? CurrentArchivePath;
    protected readonly List<HpiFileEntry> ExtractedFiles = [];

    public abstract HpiArchive Read(string hpiPath, bool quickRead = true);
    public abstract byte[] Extract(string relativePath);

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
}

