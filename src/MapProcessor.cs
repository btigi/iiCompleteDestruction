using System.Globalization;
using ii.CompleteDestruction.Model.Tdf;
using ii.CompleteDestruction.Model.Tnt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using static ii.CompleteDestruction.GafProcessor;

namespace ii.CompleteDestruction;

public class MapProcessor
{
    private const int TileWidth = 32;
    private const int TileHeight = 32;
    private const int FeatureGridScale = 16;

    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    public Image RenderMapWithAnimations(
        TntFile tntFile,
        TaFile? mapTdf,
        List<TaFile> featureTdfs,
        List<GafImageEntry> gafEntries,
        int frameIndex = 0)
    {
        var resultMap = tntFile.Map.Clone(ctx => { });

        if (gafEntries.Count == 0)
        {
            Console.WriteLine("No GAF entries supplied, returning base map.");
            return resultMap;
        }

        var gafLookup = BuildGafLookup(gafEntries);
        var featureDefinitions = BuildFeatureDefinitions(featureTdfs);
        var renderLookup = BuildFeatureRenderLookup(tntFile.TileAnimations, featureDefinitions, gafLookup);

        var (attrWidth, attrHeight) = GetAttributeGridSize(tntFile);

        var placements = new List<FeaturePlacement>();
        placements.AddRange(ExtractPlacementsFromAttributes(tntFile, attrWidth, attrHeight));
        placements.AddRange(ExtractPlacementsFromOta(mapTdf));

        if (placements.Count == 0)
        {
            Console.WriteLine("No feature placements found.");
            return resultMap;
        }

        var drawCommands = BuildDrawCommands(
            placements,
            renderLookup,
            featureDefinitions,
            gafLookup,
            tntFile.MapAttributes,
            attrWidth,
            attrHeight,
            frameIndex);

        Console.WriteLine($"Applying {drawCommands.Count} feature overlays.");

        foreach (var command in drawCommands)
        {
            OverlayFrame(resultMap, command.Frame, command.Position);
        }

        return resultMap;
    }

    private static Dictionary<string, GafImageEntry> BuildGafLookup(IEnumerable<GafImageEntry> gafEntries)
    {
        var lookup = new Dictionary<string, GafImageEntry>(NameComparer);
        foreach (var entry in gafEntries)
        {
            var normalized = NormalizeName(entry.Name);
            if (!lookup.ContainsKey(normalized))
            {
                lookup.Add(normalized, entry);
            }
        }

        return lookup;
    }

    private static Dictionary<string, FeatureDefinition> BuildFeatureDefinitions(IEnumerable<TaFile> featureTdfs)
    {
        var definitions = new Dictionary<string, FeatureDefinition>(NameComparer);

        foreach (var tdf in featureTdfs)
        {
            foreach (var block in tdf.Blocks)
            {
                CollectDefinitions(block, definitions);
            }
        }

        return definitions;
    }

    private static void CollectDefinitions(Block block, IDictionary<string, FeatureDefinition> target)
    {
        if (block.Properties.Count > 0)
        {
            var propertyBag = block.Properties
                .GroupBy(p => p.Key, NameComparer)
                .ToDictionary(g => g.Key, g => g.Last().Value, NameComparer);

            var hasRenderableData =
                propertyBag.ContainsKey("seqname") ||
                propertyBag.ContainsKey("filename") ||
                propertyBag.ContainsKey("object");

            if (hasRenderableData)
            {
                var name = block.SectionName.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    var footprintX = ParseInt(propertyBag, "footprintx", 1);
                    var footprintY = ParseInt(propertyBag, "footprintz", 1);
                    var seqName = propertyBag.TryGetValue("seqname", out var seq) ? seq : null;

                    target[NormalizeName(name)] = new FeatureDefinition(
                        name,
                        footprintX,
                        footprintY,
                        seqName);
                }
            }
        }

        foreach (var child in block.Blocks)
        {
            CollectDefinitions(child, target);
        }
    }

    private static Dictionary<string, FeatureRenderInfo> BuildFeatureRenderLookup(
        IList<TileAnimation> tileAnimations,
        Dictionary<string, FeatureDefinition> featureDefinitions,
        Dictionary<string, GafImageEntry> gafLookup)
    {
        var result = new Dictionary<string, FeatureRenderInfo>(NameComparer);

        foreach (var animation in tileAnimations)
        {
            if (string.IsNullOrWhiteSpace(animation.Name))
            {
                continue;
            }

            if (TryCreateRenderInfo(animation.Name, featureDefinitions, gafLookup, out var info))
            {
                result[NormalizeName(animation.Name)] = info;
            }
        }

        return result;
    }

    private static bool TryCreateRenderInfo(
        string featureName,
        Dictionary<string, FeatureDefinition> featureDefinitions,
        Dictionary<string, GafImageEntry> gafLookup,
        out FeatureRenderInfo renderInfo)
    {
        renderInfo = null!;
        FeatureDefinition? definition = null;
        var normalized = NormalizeName(featureName);

        if (featureDefinitions.TryGetValue(normalized, out definition))
        {
            if (TryResolveGafEntry(definition, gafLookup, out var entry))
            {
                renderInfo = new FeatureRenderInfo(
                    definition.Name,
                    Math.Max(1, definition.FootprintX),
                    Math.Max(1, definition.FootprintY),
                    entry);
                return true;
            }
        }
        else
        {
            definition = featureDefinitions.Values.FirstOrDefault(
                d => !string.IsNullOrWhiteSpace(d.SequenceName) &&
                     NormalizeName(d.SequenceName) == normalized);

            if (definition != null && TryResolveGafEntry(definition, gafLookup, out var altEntry))
            {
                renderInfo = new FeatureRenderInfo(
                    definition.Name,
                    Math.Max(1, definition.FootprintX),
                    Math.Max(1, definition.FootprintY),
                    altEntry);
                return true;
            }
        }

        if (gafLookup.TryGetValue(normalized, out var fallbackEntry))
        {
            renderInfo = new FeatureRenderInfo(featureName, 1, 1, fallbackEntry);
            return true;
        }

        return false;
    }

    private static bool TryResolveGafEntry(
        FeatureDefinition definition,
        Dictionary<string, GafImageEntry> gafLookup,
        out GafImageEntry entry)
    {
        entry = null!;

        if (!string.IsNullOrWhiteSpace(definition.SequenceName))
        {
            var normalizedSeq = NormalizeName(definition.SequenceName);
            if (gafLookup.TryGetValue(normalizedSeq, out entry))
            {
                return true;
            }
        }

        return gafLookup.TryGetValue(NormalizeName(definition.Name), out entry);
    }

    private static (int width, int height) GetAttributeGridSize(TntFile tntFile)
    {
        if (tntFile.AttributeWidth > 0 && tntFile.AttributeHeight > 0)
        {
            return (tntFile.AttributeWidth, tntFile.AttributeHeight);
        }

        var defaultWidth = (tntFile.Map.Width / TileWidth) * 2;
        var defaultHeight = (tntFile.Map.Height / TileHeight) * 2;
        var expectedCount = defaultWidth * defaultHeight;

        if (tntFile.MapAttributes.Count == expectedCount)
        {
            return (defaultWidth, defaultHeight);
        }

        var fallbackWidth = Math.Max(1, defaultWidth / 2);
        var fallbackHeight = Math.Max(1, defaultHeight / 2);
        if (fallbackWidth * fallbackHeight == tntFile.MapAttributes.Count)
        {
            return (fallbackWidth, fallbackHeight);
        }

        var inferredWidth = tntFile.MapAttributes.Count > 0 ? tntFile.MapAttributes.Count : 1;
        var inferredHeight = Math.Max(1, tntFile.MapAttributes.Count / Math.Max(1, inferredWidth));
        return (inferredWidth, inferredHeight);
    }

    private static IEnumerable<FeaturePlacement> ExtractPlacementsFromAttributes(
        TntFile tntFile,
        int attrWidth,
        int attrHeight)
    {
        if (tntFile.MapAttributes.Count == 0 || tntFile.TileAnimations.Count == 0 || attrWidth <= 0 || attrHeight <= 0)
        {
            yield break;
        }

        var limit = Math.Min(tntFile.MapAttributes.Count, attrWidth * attrHeight);
        for (var index = 0; index < limit; index++)
        {
            var attr = tntFile.MapAttributes[index];

            if (attr.TileAnimationIndex >= TntProcessor.FeatureVoid ||
                attr.TileAnimationIndex >= tntFile.TileAnimations.Count)
            {
                continue;
            }

            var featureName = tntFile.TileAnimations[attr.TileAnimationIndex].Name;
            if (string.IsNullOrWhiteSpace(featureName))
            {
                continue;
            }

            var x = index % attrWidth;
            var y = index / attrWidth;

            yield return new FeaturePlacement(featureName, x, y);
        }
    }

    private static IEnumerable<FeaturePlacement> ExtractPlacementsFromOta(TaFile? mapTdf)
    {
        if (mapTdf == null)
        {
            yield break;
        }

        var global = mapTdf.Blocks.FirstOrDefault(b => b.SectionName.Equals("GlobalHeader", StringComparison.OrdinalIgnoreCase));
        if (global == null)
        {
            yield break;
        }

        var schema = global.Blocks.FirstOrDefault(b => b.SectionName.Equals("Schema 0", StringComparison.OrdinalIgnoreCase));
        if (schema == null)
        {
            yield break;
        }

        var featuresRoot = schema.Blocks.FirstOrDefault(b => b.SectionName.Equals("features", StringComparison.OrdinalIgnoreCase));
        if (featuresRoot == null)
        {
            yield break;
        }

        foreach (var featureBlock in featuresRoot.Blocks)
        {
            var featureName = GetPropertyValue(featureBlock, "Featurename");
            if (string.IsNullOrWhiteSpace(featureName))
            {
                continue;
            }

            if (!TryParseInt(GetPropertyValue(featureBlock, "XPos"), out var x) ||
                !TryParseInt(GetPropertyValue(featureBlock, "ZPos"), out var y))
            {
                continue;
            }

            yield return new FeaturePlacement(featureName, x, y);
        }
    }

    private static List<DrawCommand> BuildDrawCommands(
        IEnumerable<FeaturePlacement> placements,
        Dictionary<string, FeatureRenderInfo> renderLookup,
        Dictionary<string, FeatureDefinition> featureDefinitions,
        Dictionary<string, GafImageEntry> gafLookup,
        List<MapAttribute> attributes,
        int attrWidth,
        int attrHeight,
        int frameIndex)
    {
        var commands = new List<DrawCommand>();

        foreach (var placement in placements)
        {
            if (!TryGetRenderInfo(
                    placement.FeatureName,
                    renderLookup,
                    featureDefinitions,
                    gafLookup,
                    out var renderInfo))
            {
                continue;
            }

            if (renderInfo.Entry.Frames.Count == 0)
            {
                continue;
            }

            var frame = renderInfo.Entry.Frames[frameIndex % renderInfo.Entry.Frames.Count];
            var position = CalculatePosition(placement, renderInfo, frame, attributes, attrWidth, attrHeight);

            commands.Add(new DrawCommand(frame, position));
        }

        commands.Sort((a, b) =>
        {
            if (a.Position.Y != b.Position.Y)
            {
                return a.Position.Y.CompareTo(b.Position.Y);
            }

            return a.Position.X.CompareTo(b.Position.X);
        });

        return commands;
    }

    private static bool TryGetRenderInfo(
        string featureName,
        Dictionary<string, FeatureRenderInfo> renderLookup,
        Dictionary<string, FeatureDefinition> featureDefinitions,
        Dictionary<string, GafImageEntry> gafLookup,
        out FeatureRenderInfo renderInfo)
    {
        var normalized = NormalizeName(featureName);
        if (renderLookup.TryGetValue(normalized, out renderInfo!))
        {
            return true;
        }

        if (TryCreateRenderInfo(featureName, featureDefinitions, gafLookup, out renderInfo!))
        {
            renderLookup[normalized] = renderInfo;
            return true;
        }

        return false;
    }

    private static Point CalculatePosition(
        FeaturePlacement placement,
        FeatureRenderInfo renderInfo,
        GafFrame frame,
        List<MapAttribute> attributes,
        int attrWidth,
        int attrHeight)
    {
        var clampedX = Math.Clamp(placement.GridX, 0, Math.Max(0, attrWidth - 1));
        var clampedY = Math.Clamp(placement.GridY, 0, Math.Max(0, attrHeight - 1));

        var footprintX = Math.Max(1, renderInfo.FootprintX);
        var footprintY = Math.Max(1, renderInfo.FootprintY);

        var baseX = (clampedX * FeatureGridScale) + (footprintX * FeatureGridScale / 2);
        var baseY = (clampedY * FeatureGridScale) + (footprintY * FeatureGridScale / 2);

        var heightOffset = attributes.Count == attrWidth * attrHeight
            ? ComputeAverageHeight(attributes, attrWidth, attrHeight, clampedX, clampedY) / 2
            : 0;

        baseY -= heightOffset;

        var drawX = baseX - frame.XOffset;
        var drawY = baseY - frame.YOffset;

        return new Point(drawX, drawY);
    }

    private static int ComputeAverageHeight(List<MapAttribute> attributes, int width, int height, int x, int y)
    {
        if (attributes.Count == 0 || width <= 0 || height <= 0)
        {
            return 0;
        }

        int Sample(int sx, int sy)
        {
            var clampedX = Math.Clamp(sx, 0, Math.Max(0, width - 1));
            var clampedY = Math.Clamp(sy, 0, Math.Max(0, height - 1));
            var index = clampedY * width + clampedX;
            if (index < 0 || index >= attributes.Count)
            {
                return 0;
            }

            return attributes[index].Elevation;
        }

        var topLeft = Sample(x, y);
        var topRight = Sample(x + 1, y);
        var bottomLeft = Sample(x, y + 1);
        var bottomRight = Sample(x + 1, y + 1);

        return (topLeft + topRight + bottomLeft + bottomRight) / 4;
    }

    private static void OverlayFrame(Image targetMap, GafFrame frame, Point location)
    {
        targetMap.Mutate(ctx =>
        {
            ctx.DrawImage(frame.Image, location, 1f);
        });
    }

    private static int ParseInt(IDictionary<string, string> bag, string key, int fallback)
    {
        if (bag.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static string? GetPropertyValue(Block block, string key) =>
        block.Properties.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static bool TryParseInt(string? value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith(".gaf", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.ToLowerInvariant();
    }

    private sealed record FeatureDefinition(
        string Name,
        int FootprintX,
        int FootprintY,
        string? SequenceName);

    private sealed record FeatureRenderInfo(
        string FeatureName,
        int FootprintX,
        int FootprintY,
        GafImageEntry Entry);

    private readonly record struct FeaturePlacement(
        string FeatureName,
        int GridX,
        int GridY);

    private sealed record DrawCommand(GafFrame Frame, Point Position);
}