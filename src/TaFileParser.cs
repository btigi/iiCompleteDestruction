using ii.TotalAnnihilation.Model.Tdf;
using System.Text.RegularExpressions;

namespace ii.TotalAnnihilation.Model;

public class TaFileParser
{
    // We should make this an abstract base class, with a concrete implementations for TDF, FBI and GUI

    //TODO: Do we care about case-sensitivity
    //TODO: Do we want a soft-coded list of permissable properties (and indicate the type)

    public TaFile Parse(string filePath)
    {
        var fileContent = File.ReadAllText(filePath, System.Text.Encoding.Latin1);

        // Strip out multi-line comments
        fileContent = Regex.Replace(fileContent, "/\\*.*?\\*/", " ", RegexOptions.Singleline);
        var lines = fileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(line => line.Trim())
                               .ToArray();

        // Strip out single-line comments
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.IndexOf("//") > -1)
            {
                lines[i] = line.Substring(0, line.IndexOf("//"));
            }
        }

        var index = 0;
        var result = new TaFile();
        while (index < lines.Length)
        {
            var block = ParseBlock(lines, ref index);
            result.Blocks.Add(block);
        }
        if (result.Blocks.Count > 0)
        {
            var x = result.Blocks.First().Blocks;
            result.Blocks.Clear();
            result.Blocks.AddRange(x);
        }
        return result;
    }

    private Block ParseBlock(string[] lines, ref int index)
    {
        var currentBlock = new Block();
        while (index < lines.Length)
        {
            var line = lines[index].Trim();
            if (line == "}")
            {
                index++;
                break;
            }

            if (line.StartsWith("["))
            {
                index++;
                var SectionName = line.Trim().Trim('[', ']');
                var subBlock = ParseBlock(lines, ref index);
                subBlock.SectionName = SectionName;
                currentBlock.Blocks.Add(subBlock);
            }
            else if (line.Contains('='))
            {
                var parts = line.Split('=', 2);
                var key = parts[0].Trim();
                if (!parts[1].EndsWith(';'))
                {
                    //TODO: Validation error
                }
                var value = parts[1].Trim().TrimEnd(';');
                currentBlock.Properties[key] = value;
                index++;
            }
            else
            {
                index++;
            }
        }

        return currentBlock;
    }
}