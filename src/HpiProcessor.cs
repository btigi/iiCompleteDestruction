using ii.CompleteDestruction.Model.Hpi;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ii.CompleteDestruction;

public class HpiProcessor
{
    private const uint Signature = 0x49504148; // HAPI

    private HPIVERSION _hpiVersion;
    private int _key;
    private byte[] _directory = null!;
    private FileStream _hpiFile = null!;
    private readonly List<HpiFileEntry> _extractedFiles = [];

    public HpiArchive Process(string hpiPath)
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

    private void ReadVersionHeader()
    {
        var versionBytes = new byte[Marshal.SizeOf<HPIVERSION>()];
        _hpiFile.Read(versionBytes, 0, versionBytes.Length);

        var handle = GCHandle.Alloc(versionBytes, GCHandleType.Pinned);
        try
        {
            _hpiVersion = Marshal.PtrToStructure<HPIVERSION>(handle.AddrOfPinnedObject());
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

    private static int LZ77Decompress(byte[] output, byte[] input, HPICHUNK chunk)
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

    private static int ZLibDecompress(byte[] outData, byte[] inData, HPICHUNK chunk)
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

    private int Decompress(byte[] output, byte[] input, HPICHUNK chunk)
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

    private static int CalculateChecksum(byte[] input, HPICHUNK chunk)
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

            var chunk = ByteArrayToStructure<HPICHUNK>(chunkBytes, 0);
            offset += chunkSizes[i];

            var chunkHeaderSize = Marshal.SizeOf<HPICHUNK>();
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
            var currentEntryOffset = entryListOffset + i * Marshal.SizeOf<HPIENTRY>();
            var entry = ByteArrayToStructure<HPIENTRY>(_directory, currentEntryOffset);

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

        var headerBytes = new byte[Marshal.SizeOf<HPIHEADER1>()];
        _hpiFile.Read(headerBytes, 0, headerBytes.Length);
        
        var handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
        try
        {
            var header = Marshal.PtrToStructure<HPIHEADER1>(handle.AddrOfPinnedObject());
            
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
}