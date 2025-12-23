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
}