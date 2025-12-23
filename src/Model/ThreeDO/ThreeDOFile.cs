namespace ii.CompleteDestruction.Model.ThreeDO;

public class ThreeDOFile
{
    public ThreeDOObject RootObject { get; set; } = null!;
    
    public List<ThreeDOObject> GetAllObjects()
    {
        var objects = new List<ThreeDOObject>();
        CollectObjects(RootObject, objects);
        return objects;
    }

    public List<(ThreeDOObject Object, ThreeDOObject? Parent)> GetObjectsWithParents()
    {
        var objects = new List<(ThreeDOObject, ThreeDOObject?)>();
        CollectObjectsWithParents(RootObject, null, objects);
        return objects;
    }

    public int GetTotalVertexCount()
    {
        return GetAllObjects().Sum(obj => obj.NumberOfVertexes);
    }

    public int GetTotalPrimitiveCount()
    {
        return GetAllObjects().Sum(obj => obj.NumberOfPrimitives);
    }

    public List<string> GetTextureNames()
    {
        var textures = new HashSet<string>();
        foreach (var obj in GetAllObjects())
        {
            foreach (var primitive in obj.Primitives)
            {
                if (!string.IsNullOrEmpty(primitive.TextureName))
                {
                    textures.Add(primitive.TextureName);
                }
            }
        }
        return textures.ToList();
    }

    private void CollectObjects(ThreeDOObject? obj, List<ThreeDOObject> objects)
    {
        if (obj == null) return;
        
        objects.Add(obj);
        
        if (obj.Child != null)
        {
            CollectObjects(obj.Child, objects);
        }
        
        if (obj.Sibling != null)
        {
            CollectObjects(obj.Sibling, objects);
        }
    }

    private void CollectObjectsWithParents(ThreeDOObject? obj, ThreeDOObject? parent, List<(ThreeDOObject, ThreeDOObject?)> objects)
    {
        if (obj == null) return;
        
        objects.Add((obj, parent));
        
        if (obj.Child != null)
        {
            CollectObjectsWithParents(obj.Child, obj, objects);
        }
        
        if (obj.Sibling != null)
        {
            CollectObjectsWithParents(obj.Sibling, parent, objects);
        }
    }
}