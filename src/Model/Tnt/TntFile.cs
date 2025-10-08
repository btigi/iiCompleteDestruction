using SixLabors.ImageSharp;

namespace ii.CompleteDestruction;

public partial class TntProcessor
{
    public class TntFile
    {
        public Image Map { get; set; }
        public Image Minimap { get; set; }
        public UInt32 SeaLevel { get; set; }
        public List<MapAttribute> MapAttributes { get; set; } = [];
        public List<TileAnimation> TileAnimations { get; set; } = [];
    }
}