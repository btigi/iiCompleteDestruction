using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ii.TotalAnnihilation.Model;

public class HpiDumper
{
    private const uint HEX_HAPI = 0x49504148;
    private const uint HEX_BANK = 0x4B4E4142;
    private const uint HEX_SQSH = 0x48535153;

    private static HPIVERSION hpiVersion;
    private static int Key;
    private static byte[] Directory;
    private static FileStream HPIFile;
    private static bool debug = false;
    private static List<string> OutSpec = [];

    public void Process(string hpiName, string outDir)
    {
        try
        {
            HPIFile = File.Open(hpiName, FileMode.Open, FileAccess.Read);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error opening HPI file: {0}", e.Message);
            return;
        }

        var versionBytes = new byte[Marshal.SizeOf(typeof(HPIVERSION))];
        HPIFile.Read(versionBytes, 0, Marshal.SizeOf(typeof(HPIVERSION)));

        var handle = GCHandle.Alloc(versionBytes, GCHandleType.Pinned);
        hpiVersion = (HPIVERSION)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(HPIVERSION));
        handle.Free();

        HPIFile.Seek(0, SeekOrigin.Begin);

        if (hpiVersion.HPIMarker == HEX_HAPI)
        {
            DumpV1(hpiName, outDir);
        }
        else
        {
            Console.WriteLine("Unknown HPI version");
        }

        HPIFile.Close();
    }

    private static bool StarMatch(string dat, string pat, string[] res = null)
    {
        pat = pat.ToLower();
        dat = dat.ToLower();

        List<string> resList = (res != null) ? new List<string>(res) : null;
        int nres = 0;
        string star = null;
        var starend = 0;

        var dati = 0;
        var pati = 0;

        while (true)
        {
            if (pati < pat.Length && pat[pati] == '*')
            {
                star = pat.Substring(pati + 1);
                starend = dati;

                if (res != null)
                {
                    nres++;
                    if (resList.Count <= nres)
                    { 
                        resList.Add("");
                    }
                }

                pati++;
            }
            else
            {
                var c1 = (dati < dat.Length) ? dat[dati] : '\0';
                var c2 = (pati < pat.Length) ? pat[pati] : '\0';

                if (pati < pat.Length && c2 == '\\')
                {
                    pati++;
                    c2 = (pati < pat.Length) ? pat[pati] : '\0';
                }
                else
                {
                    if (c2 == '?')
                        c2 = c1;
                }

                if (c1 == c2 && c1 != '\0')
                {
                    if (pati == pat.Length - 1 && dati == dat.Length - 1)
                        return true;

                    pati++;
                    dati++;
                }
                else
                {
                    if (dati >= dat.Length)
                        return false;

                    if (star == null)
                        return false;

                    pati = 0;
                    if (res != null)
                    {
                        if (resList.Count <= nres)
                            resList.Add("");
                        resList[nres] += dat[starend];
                    }
                    dati = ++starend;
                }
            }
        }
    }

    private static void CreatePath(string path, string outDir)
    {
        var o = string.Empty;

        if (path != outDir)
        {
            o = Path.Combine(o, outDir);
        }

        foreach (string dir in path.Split('\\'))
        {
            o = Path.Combine(o, dir);
            if (!System.IO.Directory.Exists(o))
            {
                System.IO.Directory.CreateDirectory(o);
            }
        }
    }

    private static int ReadAndDecrypt(int fpos, byte[] buff, int buffsize)
    {
        HPIFile.Seek(fpos, SeekOrigin.Begin);
        var result = HPIFile.Read(buff, 0, buffsize);

        if (Key != 0)
        {
            for (int count = 0; count < buffsize; count++)
            {
                var tkey = (fpos + count) ^ Key;
                buff[count] = (byte)(tkey ^ ~buff[count]);
            }
        }
        return result;
    }

    private static int LZ77Decompress(byte[] output, byte[] input, HPICHUNK chunk)
    {
        int x;
        int work1;
        int work2;
        int work3;
        int inptr;
        int outptr;
        int count;
        bool done;
        var DBuff = new byte[4096];
        int DPtr;

        done = false;
        inptr = 0;
        outptr = 0;
        work1 = 1;
        work2 = 1;
        work3 = input[inptr++];

        while (!done)
        {
            if ((work2 & work3) == 0)
            {
                output[outptr++] = input[inptr];
                DBuff[work1] = input[inptr];
                work1 = (work1 + 1) & 0xFFF;
                inptr++;
            }
            else
            {
                count = BitConverter.ToUInt16(input, inptr);
                inptr += 2;
                DPtr = count >> 4;
                if (DPtr == 0)
                {
                    return outptr;
                }
                else
                {
                    count = (count & 0x0f) + 2;
                    if (count >= 0)
                    {
                        for (x = 0; x < count; x++)
                        {
                            output[outptr++] = DBuff[DPtr];
                            DBuff[work1] = DBuff[DPtr];
                            DPtr = (DPtr + 1) & 0xFFF;
                            work1 = (work1 + 1) & 0xFFF;
                        }
                    }
                }
            }
            work2 *= 2;
            if (work2 > 0xFF)
            {
                work2 = 1;
                work3 = input[inptr++];
            }
        }
        return outptr;
    }

    private static int ZLibDecompress(byte[] outData, byte[] inData, HPICHUNK chunk)
    {
        using var compressedStream = new MemoryStream(inData);
        using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        deflateStream.CopyTo(outputStream);
        byte[] decompressedBytes = outputStream.ToArray();
        Array.Copy(decompressedBytes, outData, decompressedBytes.Length);
        return decompressedBytes.Length;
    }

    private static int Decompress(byte[] output, byte[] input, HPICHUNK chunk)
    {
        var Checksum = 0;
        for (int x = 0; x < chunk.CompressedSize; x++)
        {
            Checksum += (byte)input[x];
            if (chunk.Encrypt != 0)
            {
                input[x] = (byte)((uint)input[x] - x ^ x);
            }
        }

        if (debug)
        {
            Console.WriteLine("Unknown1                  0x{0:X2}", chunk.Unknown1);
            Console.WriteLine("CompMethod:               {0}", chunk.CompMethod);
            Console.WriteLine("Encrypt:                  {0}", chunk.Encrypt);
            Console.WriteLine("CompressedSize:           {0}", chunk.CompressedSize);
            Console.WriteLine("DecompressedSize:         {0}", chunk.DecompressedSize);
            Console.WriteLine("SQSH Checksum:            0x{0:X}", chunk.Checksum);
            Console.WriteLine("Calculated SQSH checksum: 0x{0:X}", Checksum);
        }

        if (chunk.Checksum != Checksum)
        {
            Console.WriteLine("*** SQSH checksum error! Calculated: 0x{0:X}  Actual: 0x{1:X}", Checksum, chunk.Checksum);
            return 0;
        }

        return chunk.CompMethod switch
        {
            1 => LZ77Decompress(output, input, chunk),
            2 => ZLibDecompress(output, input, chunk),
            _ => 0,
        };
    }

    private static void ProcessFile(string TName, int ofs, int len, int FileFlag, string outDir)
    {
        HPICHUNK Chunk;
        int DeCount;
        int x;
        byte[] DeBuff;
        byte[] WriteBuff;
        int WriteSize;
        int DeTotal;
        int CTotal;
        int DeLen;
        FileStream Sub;
        var Name = Path.Combine(outDir, TName);

        try
        {
            Sub = File.Open(Name, FileMode.Create, FileAccess.Write);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error creating '{0}': {1}", Name, e.Message);
            return;
        }

        Console.WriteLine("{0} -> {1}", TName, Name);

        if (debug)
            Console.WriteLine("Offset 0x{0:X}", ofs);

        if (FileFlag != 0 || true)
        {
            DeCount = len / 65536;
            if (len % 65536 != 0)
            { 
                DeCount++;
            }
            DeLen = DeCount * sizeof(int);
            var DeSize = new int[DeCount];
            DeTotal = 0;
            CTotal = 0;

            var desizeBytes = new byte[DeLen];
            ReadAndDecrypt(ofs, desizeBytes, DeLen);
            Buffer.BlockCopy(desizeBytes, 0, DeSize, 0, DeLen);

            ofs += DeLen;

            if (debug)
                Console.WriteLine("\nChunks: {0}", DeCount);

            WriteBuff = new byte[65536];

            for (x = 0; x < DeCount; x++)
            {
                Chunk = new HPICHUNK();
                var chunkBytes = new byte[DeSize[x]];
                ReadAndDecrypt(ofs, chunkBytes, DeSize[x]);

                var handle = GCHandle.Alloc(chunkBytes, GCHandleType.Pinned);
                Chunk = (HPICHUNK)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(HPICHUNK));
                handle.Free();

                if (debug)
                {
                    Console.WriteLine("Chunk {0}: Compressed {1}  Decompressed {2}  SQSH Checksum 0x{3:X}", x + 1, Chunk.CompressedSize, Chunk.DecompressedSize, Chunk.Checksum);
                    Console.WriteLine("   Unknown1: 0x{0:X2} CompMethod: 0x{1:X2} Encrypt: 0x{2:X2}", Chunk.Unknown1, Chunk.CompMethod, Chunk.Encrypt);
                }

                CTotal += Chunk.CompressedSize;
                DeTotal += Chunk.DecompressedSize;

                ofs += DeSize[x];

                DeBuff = new byte[chunkBytes.Length - Marshal.SizeOf(typeof(HPICHUNK))];
                Buffer.BlockCopy(chunkBytes, Marshal.SizeOf(typeof(HPICHUNK)), DeBuff, 0, chunkBytes.Length - Marshal.SizeOf(typeof(HPICHUNK)));

                WriteSize = Decompress(WriteBuff, DeBuff, Chunk);

                Sub.Write(WriteBuff, 0, WriteSize);
                if (WriteSize != Chunk.DecompressedSize)
                {
                    Console.WriteLine("WriteSize ({0}) != Chunk.DecompressedSize ({1})!", WriteSize, Chunk.DecompressedSize);
                }
            }
            Sub.Close();

            if (debug)
                Console.WriteLine("Total compressed: {0}  Total decompressed: {1}\n", CTotal, DeTotal);
        }
        else
        {
            WriteBuff = new byte[len];
            var encryptedData = new byte[len];
            ReadAndDecrypt(ofs, encryptedData, len);
            Buffer.BlockCopy(encryptedData, 0, WriteBuff, 0, len);
            Sub.Write(WriteBuff, 0, len);
            Sub.Close();
        }
    }

    private static void ProcessDirectory(string StartPath, int offset, string outDir)
    {
        int Entries;
        HPIENTRY Entry;
        int count;
        string Name;
        int FileCount;
        int FileLength;
        byte FileFlag;
        string MyPath = "";
        string MyDir = "";
        bool extract;
        int SCount;

        Entries = BitConverter.ToInt32(Directory, offset);
        var EntryOffset = BitConverter.ToInt32(Directory, offset + 4); // Read the offset to the entry list

        EntryOffset = EntryOffset - 20;

        for (count = 0; count < Entries; count++)
        {
            var entryOffset = EntryOffset + count * Marshal.SizeOf(typeof(HPIENTRY));
            Entry = ByteArrayToStructure<HPIENTRY>(Directory, entryOffset);

            Name = ReadString(Directory, Entry.NameOffset - 20);

            FileCount = BitConverter.ToInt32(Directory, Entry.CountOffset - 20);

            if (!string.IsNullOrEmpty(StartPath))
            {
                MyPath = Path.Combine(StartPath, Name);
            }
            else
            {
                MyPath = Name;
            }

            if (Entry.Flag == 1)
            {
                if (debug)
                    Console.WriteLine("Directory {0} Files {1} Flag {2}", Name, FileCount, Entry.Flag);

                if (OutSpec.Count == 0)
                {
                    MyDir = Path.Combine(outDir, MyPath);
                    System.IO.Directory.CreateDirectory(MyDir);
                }
                ProcessDirectory(MyPath, Entry.CountOffset - 20, outDir);
            }
            else
            {
                FileLength = BitConverter.ToInt32(Directory, Entry.CountOffset - 20 + 4);
                FileFlag = Directory[Entry.CountOffset + 8];
                extract = true;
                if (OutSpec.Count > 0)
                {
                    SCount = 0;
                    extract = false;
                    foreach (string spec in OutSpec)
                    {
                        if (StarMatch(Name, spec, null) || StarMatch(MyPath, spec, null))
                        {
                            extract = true;
                            break;
                        }
                        SCount++;
                    }

                    if (extract)
                    {
                        CreatePath(StartPath, outDir);
                    }
                }

                if (extract)
                {
                    if (debug)
                        Console.WriteLine("File {0} Data Offset {1} Length {2} Flag {3} FileFlag {4}", Name, FileCount, FileLength, Entry.Flag, FileFlag);

                    ProcessFile(MyPath, FileCount, FileLength, FileFlag, outDir);
                }
            }
        }
    }

    private static string ReadString(byte[] data, int offset)
    {
        var bytes = new List<byte>();
        byte b;
        int i = offset;
        while ((b = data[i++]) != 0)
        { 
            bytes.Add(b);
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static T ByteArrayToStructure<T>(byte[] buffer, int offset) where T : struct
    {
        var size = Marshal.SizeOf(typeof(T));
        if (buffer.Length < size + offset)
        {
            throw new ArgumentException("Buffer is too small to hold the structure.", "buffer");
        }

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(buffer, offset, ptr, size);
            return (T)Marshal.PtrToStructure(ptr, typeof(T));
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static bool CheckDirectory(string DName, string outDir)
    {
        if (string.IsNullOrEmpty(DName))
        {
            DName = ".";
            return true;
        }
        CreatePath(DName, outDir);
        return true;
    }

    private static void DumpV1(string hpiName, string outDir)
    {
        HPIFile.Seek(8, SeekOrigin.Begin);

        var headerBytes = new byte[Marshal.SizeOf(typeof(HPIHEADER1))];
        HPIFile.Read(headerBytes, 0, Marshal.SizeOf(typeof(HPIHEADER1)));
        var handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
        var h1 = (HPIHEADER1)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(HPIHEADER1));
        handle.Free();

        if (!CheckDirectory(outDir, outDir))
        {
            return;
        }

        if (!string.IsNullOrEmpty(outDir))
        { 
            Console.WriteLine("Extracting {0} to {1}", hpiName, outDir);
        }
        else
        { 
            Console.WriteLine("Extracting {0}", hpiName);
        }

        if (h1.Key != 0)
        { 
            Key = ~((h1.Key * 4) | (h1.Key >> 6));
        }
        else
        { 
            Key = 0;
        }

        Directory = new byte[h1.DirectorySize];
        ReadAndDecrypt(h1.Start, Directory, h1.DirectorySize - h1.Start);
        ProcessDirectory("", 0, outDir);
    }
}