using ii.CompleteDestruction.Model.Tdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace ii.CompleteDestruction;

public class MapProcessor
{
    private const int TileWidth = 32;
    private const int TileHeight = 32;

    public Image RenderMapWithAnimations(
        TntFile tntFile,
        TaFile? mapTdf,
        List<TaFile> featureTdfs,
        List<GafImageEntry> gafEntries,
        int frameIndex = 0)
    {
        var resultMap = tntFile.Map.Clone(ctx => { });

        if (tntFile.TileAnimations.Count == 0 || gafEntries.Count == 0)
        {
            Console.WriteLine("No animations to apply (either no tile animations or no GAF entries)");
            return resultMap;
        }

        Console.WriteLine($"=== Map Animation Processing ===");
        Console.WriteLine($"Map size: {tntFile.Map.Width}x{tntFile.Map.Height}");
        Console.WriteLine($"Tile animations defined in TNT: {tntFile.TileAnimations.Count}");
        Console.WriteLine($"Map attributes (tiles): {tntFile.MapAttributes.Count}");
        Console.WriteLine($"GAF entries available: {gafEntries.Count}");

        // Animation name to GAF entry lookup
        var animationLookup = BuildAnimationLookup(tntFile.TileAnimations, featureTdfs, gafEntries);

        if (animationLookup.Count == 0)
        {
            Console.WriteLine("No animations matched :(");
            return resultMap;
        }

        Console.WriteLine($"Matched {animationLookup.Count} animations");

        // Apply animations to the map
        var mapWidthInTiles = tntFile.Map.Width / TileWidth;
        var mapHeightInTiles = tntFile.Map.Height / TileHeight;

        Console.WriteLine($"Map dimensions: {mapWidthInTiles}x{mapHeightInTiles} tiles");
        Console.WriteLine($"Total map attributes: {tntFile.MapAttributes.Count}");
        Console.WriteLine($"Total tile animations: {tntFile.TileAnimations.Count}");
               
        var indexCounts = new Dictionary<ushort, int>();
        foreach (var attr in tntFile.MapAttributes)
        {
            if (!indexCounts.ContainsKey(attr.TileAnimationIndex))
                indexCounts[attr.TileAnimationIndex] = 0;
            indexCounts[attr.TileAnimationIndex]++;
        }
        
        var animatedTileCount = 0;
        var tilesWithAnimationIndex = 0;
        var animationsNotFound = 0;
        
        for (var tileY = 0; tileY < mapHeightInTiles; tileY++)
        {
            for (var tileX = 0; tileX < mapWidthInTiles; tileX++)
            {
                var attrIndex = tileY * mapWidthInTiles + tileX;
                if (attrIndex >= tntFile.MapAttributes.Count)
                    continue;

                var mapAttr = tntFile.MapAttributes[attrIndex];

                // Check if this tile has an animation (0xFFFF = no animation)
                if (mapAttr.TileAnimationIndex != 0xFFFF && mapAttr.TileAnimationIndex < tntFile.TileAnimations.Count)
                {
                    tilesWithAnimationIndex++;
                                       
                    // TileAnimationIndex is an array index into TileAnimations (not the animation's Index field)?
                    var animation = tntFile.TileAnimations[mapAttr.TileAnimationIndex];
                    
                    if (animationLookup.TryGetValue(animation.Index, out var gafEntry))
                    {
                        // Get the appropriate frame (cycle if frameIndex exceeds frame count)
                        var frame = gafEntry.Frames[frameIndex % gafEntry.Frames.Count];
                        // Overlay the animation frame onto the map at the tile position
                        OverlayFrame(resultMap, frame, tileX * TileWidth, tileY * TileHeight);
                        animatedTileCount++;
                    }
                    else
                    {
                        animationsNotFound++;
                    }
                }
            }
        }

        Console.WriteLine($"Animation Application Summary:");
        Console.WriteLine($"  Tiles with animation index: {tilesWithAnimationIndex}");
        Console.WriteLine($"  Successfully applied: {animatedTileCount}");
        Console.WriteLine($"  Animations not found: {animationsNotFound}");
        return resultMap;
    }

    private Dictionary<int, GafImageEntry> BuildAnimationLookup(
        List<TileAnimation> tileAnimations,
        List<TaFile> featureTdfs,
        List<GafImageEntry> gafEntries)
    {
        var lookup = new Dictionary<int, GafImageEntry>();

        Console.WriteLine("--- Building Animation Lookup ---");
        Console.WriteLine("TNT Tile Animations:");
        foreach (var anim in tileAnimations)
        {
            Console.WriteLine($"  Index {anim.Index}: Name='{anim.Name}'");
        }

        Console.WriteLine("Available GAF Entries:");
        foreach (var gaf in gafEntries)
        {
            Console.WriteLine($"  '{gaf.Name}' ({gaf.Frames.Count} frames)");
        }

        Console.WriteLine("--- Matching Process ---");

        // For each tile animation, try to find the corresponding GAF entry
        foreach (var animation in tileAnimations)
        {
            // The animation.Name might directly correspond to a GAF entry name or it might need to be looked up in the feature TDFs
            
            var animName = animation.Name.Trim().ToLowerInvariant();
            
            // Direct match (animation name == GAF entry name)
            var gafEntry = gafEntries.FirstOrDefault(g => 
                g.Name.Trim().ToLowerInvariant() == animName);

            if (gafEntry != null)
            {
                lookup[animation.Index] = gafEntry;
                Console.WriteLine($"✓ Direct match: TileAnim[{animation.Index}] '{animation.Name}' -> GAF '{gafEntry.Name}'");
                continue;
            }

            // If no direct match, try to find it in the feature TDFs
            var foundInTdf = TryMatchFromFeatureTdf(animation, featureTdfs, gafEntries, out gafEntry);
            if (foundInTdf && gafEntry != null)
            {
                lookup[animation.Index] = gafEntry;
                Console.WriteLine($"TDF match: TileAnim[{animation.Index}] '{animation.Name}' -> GAF '{gafEntry.Name}'");
            }
            else
            {
                Console.WriteLine($"No match found for TileAnim[{animation.Index}] '{animation.Name}'");
            }
        }

        return lookup;
    }

    private bool TryMatchFromFeatureTdf(
        TileAnimation animation,
        List<TaFile> featureTdfs,
        List<GafImageEntry> gafEntries,
        out GafImageEntry? matchedGaf)
    {
        matchedGaf = null;

        // Look for a block in any feature TDF that matches the animation name
        foreach (var tdf in featureTdfs)
        {
            var block = FindBlockRecursive(tdf.Blocks, animation.Name);
            if (block != null)
            {
                // Look for the 'seqname' property which contains the GAF animation name
                var seqNameProperty = block.Properties.FirstOrDefault(p => p.Key.Trim().Equals("seqname", StringComparison.OrdinalIgnoreCase));

                if (seqNameProperty != null)
                {
                    var gafName = seqNameProperty.Value.Trim().ToLowerInvariant();
                    gafName = gafName.Replace(".gaf", "");
                    matchedGaf = gafEntries.FirstOrDefault(g => g.Name.Trim().ToLowerInvariant() == gafName);

                    if (matchedGaf == null)
                    {
                        matchedGaf = gafEntries.FirstOrDefault(g => g.Name.Trim().ToLowerInvariant().Contains(gafName) || gafName.Contains(g.Name.Trim().ToLowerInvariant()));
                    }

                    if (matchedGaf != null)
                    {
                        Console.WriteLine($"    TDF lookup: [{animation.Name}] seqname='{seqNameProperty.Value}' -> GAF '{matchedGaf.Name}'");
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private Block? FindBlockRecursive(List<Block> blocks, string sectionName)
    {
        var normalizedName = sectionName.Trim().ToLowerInvariant();
        
        foreach (var block in blocks)
        {
            if (block.SectionName.Trim().ToLowerInvariant() == normalizedName)
                return block;

            var found = FindBlockRecursive(block.Blocks, sectionName);
            if (found != null)
                return found;
        }

        return null;
    }

    private void OverlayFrame(Image targetMap, GafFrame frame, int x, int y)
    {
        // Apply offsets from the GAF frame - maybe we don't need to do this?
        var targetX = x + frame.XOffset;
        var targetY = y + frame.YOffset;

        targetMap.Mutate(ctx =>
        {
            ctx.DrawImage(frame.Image, new Point(targetX, targetY), 1f);
        });
    }
}