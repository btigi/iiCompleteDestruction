using ii.CompleteDestruction.Model.ThreeDO;

namespace ii.CompleteDestruction;

public class ThreeDOProcessor
{
    private const int ExpectedVersionSignature = 1;

    public ThreeDOFile Read(string filePath)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br);
    }

    public ThreeDOFile Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br);
    }

    public void Write(string filePath, ThreeDOFile file)
    {
        using var bw = new BinaryWriter(File.Create(filePath));
        WriteObject(bw, file.RootObject);
    }

    public byte[] Write(ThreeDOFile file)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteObject(bw, file.RootObject);
        return ms.ToArray();
    }

    private ThreeDOFile Read(BinaryReader br)
    {
        var file = new ThreeDOFile
        {
            RootObject = ReadObject(br, 0)
        };

        return file;
    }

    private ThreeDOObject ReadObject(BinaryReader br, int offset)
    {
        br.BaseStream.Seek(offset, SeekOrigin.Begin);

        var obj = new ThreeDOObject
        {
            VersionSignature = br.ReadInt32(),
            NumberOfVertexes = br.ReadInt32(),
            NumberOfPrimitives = br.ReadInt32(),
            SelectionPrimitive = br.ReadInt32(),
            XFromParent = br.ReadInt32(),
            YFromParent = br.ReadInt32(),
            ZFromParent = br.ReadInt32(),
            OffsetToObjectName = br.ReadInt32(),
            Always_0 = br.ReadInt32(),
            OffsetToVertexArray = br.ReadInt32(),
            OffsetToPrimitiveArray = br.ReadInt32(),
            OffsetToSiblingObject = br.ReadInt32(),
            OffsetToChildObject = br.ReadInt32()
        };

        if (obj.VersionSignature != ExpectedVersionSignature)
        {
            throw new InvalidDataException($"Invalid 3DO version signature. Expected {ExpectedVersionSignature}, got {obj.VersionSignature}");
        }

        if (obj.OffsetToObjectName > 0)
        {
            obj.Name = ReadNullTerminatedString(br, obj.OffsetToObjectName);
        }

        if (obj.NumberOfVertexes > 0 && obj.OffsetToVertexArray > 0)
        {
            obj.Vertices = ReadVertices(br, obj.OffsetToVertexArray, obj.NumberOfVertexes);
        }

        if (obj.NumberOfPrimitives > 0 && obj.OffsetToPrimitiveArray > 0)
        {
            obj.Primitives = ReadPrimitives(br, obj.OffsetToPrimitiveArray, obj.NumberOfPrimitives);
        }

        if (obj.OffsetToChildObject > 0)
        {
            obj.Child = ReadObject(br, obj.OffsetToChildObject);
        }

        if (obj.OffsetToSiblingObject > 0)
        {
            obj.Sibling = ReadObject(br, obj.OffsetToSiblingObject);
        }

        return obj;
    }

    private ThreeDOVertex[] ReadVertices(BinaryReader br, int offset, int count)
    {
        br.BaseStream.Seek(offset, SeekOrigin.Begin);

        var vertices = new ThreeDOVertex[count];
        for (var i = 0; i < count; i++)
        {
            vertices[i] = new ThreeDOVertex
            {
                X = br.ReadInt32(),
                Y = br.ReadInt32(),
                Z = br.ReadInt32()
            };
        }

        return vertices;
    }

    private ThreeDOPrimitive[] ReadPrimitives(BinaryReader br, int offset, int count)
    {
        br.BaseStream.Seek(offset, SeekOrigin.Begin);

        var primitives = new ThreeDOPrimitive[count];
        for (var i = 0; i < count; i++)
        {
            primitives[i] = new ThreeDOPrimitive
            {
                ColorIndex = br.ReadInt32(),
                NumberOfVertexIndexes = br.ReadInt32(),
                Always_0 = br.ReadInt32(),
                OffsetToVertexIndexArray = br.ReadInt32(),
                OffsetToTextureName = br.ReadInt32(),
                Unknown_1 = br.ReadInt32(),
                Unknown_2 = br.ReadInt32(),
                Unknown_3 = br.ReadInt32()
            };

            if (primitives[i].NumberOfVertexIndexes > 0 && primitives[i].OffsetToVertexIndexArray > 0)
            {
                var currentPosition = br.BaseStream.Position;
                primitives[i].VertexIndices = ReadVertexIndices(br, primitives[i].OffsetToVertexIndexArray, primitives[i].NumberOfVertexIndexes);
                br.BaseStream.Position = currentPosition;
            }

            if (primitives[i].OffsetToTextureName > 0)
            {
                var currentPosition = br.BaseStream.Position;
                primitives[i].TextureName = ReadNullTerminatedString(br, primitives[i].OffsetToTextureName);
                br.BaseStream.Position = currentPosition;
            }
        }

        return primitives;
    }

    private short[] ReadVertexIndices(BinaryReader br, int offset, int count)
    {
        br.BaseStream.Seek(offset, SeekOrigin.Begin);

        var indices = new short[count];
        for (var i = 0; i < count; i++)
        {
            indices[i] = br.ReadInt16();
        }

        return indices;
    }

    private string ReadNullTerminatedString(BinaryReader br, int offset)
    {
        br.BaseStream.Seek(offset, SeekOrigin.Begin);

        var bytes = new List<byte>();
        byte b;
        while ((b = br.ReadByte()) != 0)
        {
            bytes.Add(b);
        }

        return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
    }

    private void WriteObject(BinaryWriter bw, ThreeDOObject obj)
    {
        var headerPosition = bw.BaseStream.Position;

        bw.Write(obj.VersionSignature);
        bw.Write(obj.Vertices.Length);
        bw.Write(obj.Primitives.Length);
        bw.Write(obj.SelectionPrimitive);
        bw.Write(obj.XFromParent);
        bw.Write(obj.YFromParent);
        bw.Write(obj.ZFromParent);
        bw.Write(0); // OffsetToObjectName — patched later
        bw.Write(obj.Always_0);
        bw.Write(0); // OffsetToVertexArray — patched later
        bw.Write(0); // OffsetToPrimitiveArray — patched later
        bw.Write(0); // OffsetToSiblingObject — patched later
        bw.Write(0); // OffsetToChildObject — patched later

        var offsetToObjectName = 0;
        if (!string.IsNullOrEmpty(obj.Name))
        {
            offsetToObjectName = (int)bw.BaseStream.Position;
            WriteNullTerminatedString(bw, obj.Name);
        }

        var offsetToVertexArray = 0;
        if (obj.Vertices.Length > 0)
        {
            offsetToVertexArray = (int)bw.BaseStream.Position;
            foreach (var vertex in obj.Vertices)
            {
                bw.Write(vertex.X);
                bw.Write(vertex.Y);
                bw.Write(vertex.Z);
            }
        }

        var offsetToPrimitiveArray = 0;
        var primitiveHeaderPositions = Array.Empty<long>();
        if (obj.Primitives.Length > 0)
        {
            offsetToPrimitiveArray = (int)bw.BaseStream.Position;
            primitiveHeaderPositions = new long[obj.Primitives.Length];

            for (var i = 0; i < obj.Primitives.Length; i++)
            {
                primitiveHeaderPositions[i] = bw.BaseStream.Position;
                var prim = obj.Primitives[i];
                bw.Write(prim.ColorIndex);
                bw.Write(prim.VertexIndices.Length);
                bw.Write(prim.Always_0);
                bw.Write(0); // OffsetToVertexIndexArray — patched later
                bw.Write(0); // OffsetToTextureName — patched later
                bw.Write(prim.Unknown_1);
                bw.Write(prim.Unknown_2);
                bw.Write(prim.Unknown_3);
            }

            for (var i = 0; i < obj.Primitives.Length; i++)
            {
                var prim = obj.Primitives[i];

                var offsetToVertexIndexArray = 0;
                if (prim.VertexIndices.Length > 0)
                {
                    offsetToVertexIndexArray = (int)bw.BaseStream.Position;
                    foreach (var index in prim.VertexIndices)
                    {
                        bw.Write(index);
                    }
                }

                var offsetToTextureName = 0;
                if (!string.IsNullOrEmpty(prim.TextureName))
                {
                    offsetToTextureName = (int)bw.BaseStream.Position;
                    WriteNullTerminatedString(bw, prim.TextureName);
                }

                var currentPosition = bw.BaseStream.Position;
                bw.BaseStream.Seek(primitiveHeaderPositions[i] + 12, SeekOrigin.Begin);
                bw.Write(offsetToVertexIndexArray);
                bw.Write(offsetToTextureName);
                bw.BaseStream.Position = currentPosition;
            }
        }

        var offsetToChildObject = 0;
        if (obj.Child != null)
        {
            offsetToChildObject = (int)bw.BaseStream.Position;
            WriteObject(bw, obj.Child);
        }

        var offsetToSiblingObject = 0;
        if (obj.Sibling != null)
        {
            offsetToSiblingObject = (int)bw.BaseStream.Position;
            WriteObject(bw, obj.Sibling);
        }

        var endPosition = bw.BaseStream.Position;
        bw.BaseStream.Seek(headerPosition + 28, SeekOrigin.Begin);
        bw.Write(offsetToObjectName);
        bw.BaseStream.Seek(headerPosition + 36, SeekOrigin.Begin);
        bw.Write(offsetToVertexArray);
        bw.Write(offsetToPrimitiveArray);
        bw.Write(offsetToSiblingObject);
        bw.Write(offsetToChildObject);
        bw.BaseStream.Position = endPosition;
    }

    private void WriteNullTerminatedString(BinaryWriter bw, string value)
    {
        bw.Write(System.Text.Encoding.ASCII.GetBytes(value));
        bw.Write((byte)0);
    }
}