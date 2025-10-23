using ii.CompleteDestruction.Model.Tdf;
using System.Text;

namespace ii.CompleteDestruction;

public partial class TaFileParser
{
    public TaFile Read(string filePath)
    {
        var fileContent = File.ReadAllText(filePath, Encoding.Latin1);
        var parsedLines = SplitIntoLines(fileContent);

        var index = 0;
        var result = new TaFile();
        
        // File header comments
        while (index < parsedLines.Count && !parsedLines[index].Content.StartsWith("["))
        {
            if (!string.IsNullOrEmpty(parsedLines[index].Comment))
            {
                result.HeaderComments = AppendComment(result.HeaderComments, parsedLines[index].Comment);
            }
            index++;
        }

        // Parse blocks
        while (index < parsedLines.Count)
        {
            if (parsedLines[index].Content.StartsWith("["))
            {
                result.Blocks.Add(ParseBlock(parsedLines, ref index));
            }
            else
            {
                index++;
            }
        }

        return result;
    }

    private static string AppendComment(string existing, string newComment)
    {
        if (string.IsNullOrEmpty(existing))
        {
            return newComment;
        }
        return existing + Environment.NewLine + newComment;
    }

    private List<ParsedLine> SplitIntoLines(string content)
    {
        // First, handle multi-line comments by replacing them with placeholders
        var commentMap = new Dictionary<string, string>();
        var commentCounter = 0;
        var inMultiLineComment = false;
        var builder = new StringBuilder();
        var commentBuilder = new StringBuilder();
        
        for (var i = 0; i < content.Length; i++)
        {
            if (inMultiLineComment)
            {
                if (i < content.Length - 1 && content[i] == '*' && content[i + 1] == '/')
                {
                    // End of multi-line comment
                    var placeholder = $"__MLCOMMENT_{commentCounter}__";
                    commentMap[placeholder] = commentBuilder.ToString();
                    builder.Append(placeholder);
                    commentBuilder.Clear();
                    inMultiLineComment = false;
                    commentCounter++;
                    i++; // Skip the '/'
                }
                else
                {
                    commentBuilder.Append(content[i]);
                }
            }
            else if (i < content.Length - 1 && content[i] == '/' && content[i + 1] == '*')
            {
                // Start of multi-line comment
                inMultiLineComment = true;
                i++; // Skip the '*'
            }
            else
            {
                builder.Append(content[i]);
            }
        }
        
        var processedContent = builder.ToString();
        var lines = processedContent.Split(['\r', '\n'], StringSplitOptions.None);
        var result = new List<ParsedLine>();

        foreach (var rawLine in lines)
        {
            var line = rawLine;
            var parsedLine = new ParsedLine();

            // Replace multi-line comment placeholders
            foreach (var kvp in commentMap)
            {
                if (line.Contains(kvp.Key))
                {
                    line = line.Replace(kvp.Key, "");
                    parsedLine.Comment = kvp.Value.Trim();
                }
            }

            // Handle single-line comments
            var commentIndex = line.IndexOf("//");
            if (commentIndex >= 0)
            {
                parsedLine.Content = line.Substring(0, commentIndex).Trim();
                var singleLineComment = line.Substring(commentIndex + 2).Trim();
                parsedLine.Comment = string.IsNullOrEmpty(parsedLine.Comment) 
                    ? singleLineComment 
                    : parsedLine.Comment + " " + singleLineComment;
            }
            else
            {
                parsedLine.Content = line.Trim();
            }

            result.Add(parsedLine);
        }

        return result;
    }

    private Block ParseBlock(List<ParsedLine> lines, ref int index)
    {
        var block = new Block();
        
        // Parse section header [SECTIONNAME]
        var remainingLineContent = string.Empty;
        if (lines[index].Content.StartsWith("["))
        {
            var content = lines[index].Content;
            var startIndex = content.IndexOf('[');
            var endIndex = content.IndexOf(']', startIndex);
            if (startIndex >= 0 && endIndex > startIndex)
            {
                block.SectionName = content.Substring(startIndex + 1, endIndex - startIndex - 1).Trim();
                // Get any content after the closing bracket
                if (endIndex + 1 < content.Length)
                {
                    remainingLineContent = content.Substring(endIndex + 1).Trim();
                }
            }
            else
            {
                // Fallback if no closing bracket found
                block.SectionName = content.Trim('[', ']', ' ', '\t');
            }
            
            if (!string.IsNullOrEmpty(lines[index].Comment))
            {
                block.Comments = AppendComment(block.Comments, lines[index].Comment);
            }
            index++;
        }

        // Check if opening brace was on the same line as section header
        if (!string.IsNullOrEmpty(remainingLineContent) && remainingLineContent.StartsWith("{"))
        {
            // Handle the brace content that was on the section header line
            if (remainingLineContent == "{}")
            {
                // Empty block
                return block;
            }
            else if (remainingLineContent.StartsWith("{") && remainingLineContent.EndsWith("}") && remainingLineContent.Length > 2)
            {
                // Single-line block like: [SECTION] { x=1; y=2; }
                var innerContent = remainingLineContent.Substring(1, remainingLineContent.Length - 2).Trim();
                var properties = innerContent.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var propText in properties)
                {
                    if (propText.Contains('='))
                    {
                        var parts = propText.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var property = new Property
                            {
                                Key = parts[0].Trim(),
                                Value = parts[1].Trim(),
                                Comment = string.Empty
                            };
                            block.Properties.Add(property);
                        }
                    }
                }
                return block;
            }
            // If it starts with { but doesn't end with }, continue parsing normally
        }

        // Skip to opening brace, collecting comments along the way
        while (index < lines.Count && !lines[index].Content.StartsWith("{"))
        {
            if (!string.IsNullOrEmpty(lines[index].Comment))
            {
                block.Comments = AppendComment(block.Comments, lines[index].Comment);
            }
            index++;
        }

        // Handle opening brace, it might have content on the same line
        if (index < lines.Count)
        {
            var braceLineContent = lines[index].Content;
            if (!string.IsNullOrEmpty(lines[index].Comment))
            {
                block.Comments = AppendComment(block.Comments, lines[index].Comment);
            }
            
            // Check if this is a single-line block like: { x1=1; x2=2; }
            if (braceLineContent.StartsWith("{") && braceLineContent.EndsWith("}") && braceLineContent.Length > 2)
            {
                // Extract content between braces
                var innerContent = braceLineContent.Substring(1, braceLineContent.Length - 2).Trim();
                
                // Split by semicolons and parse each property
                var properties = innerContent.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var propText in properties)
                {
                    if (propText.Contains('='))
                    {
                        var parts = propText.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var property = new Property
                            {
                                Key = parts[0].Trim(),
                                Value = parts[1].Trim(),
                                Comment = string.Empty
                            };
                            block.Properties.Add(property);
                        }
                    }
                }
                index++;
                return block; // Single-line block is complete
            }
            else if (braceLineContent == "{")
            {
                // Normal opening brace on its own line
                index++;
            }
            else if (braceLineContent.StartsWith("{"))
            {
                // Opening brace with content but no closing brace - treat content after { as first line
                // This case is unusual but we'll handle it by continuing to parse normally
                index++;
            }
        }

        // Parse block contents until closing brace
        while (index < lines.Count && lines[index].Content != "}")
        {
            var content = lines[index].Content;
            var comment = lines[index].Comment;

            if (string.IsNullOrWhiteSpace(content))
            {
                // Standalone comment
                if (!string.IsNullOrEmpty(comment))
                {
                    block.Comments = AppendComment(block.Comments, comment);
                }
            }
            else if (content.StartsWith("["))
            {
                // Nested block
                block.Blocks.Add(ParseBlock(lines, ref index));
                continue; // ParseBlock advances index
            }
            else if (content.Contains('='))
            {
                // Property - collect lines until we find semicolon
                var propertyText = content;
                var propertyComment = comment;
                index++;

                while (!propertyText.Contains(';') && index < lines.Count)
                {
                    var nextContent = lines[index].Content;
                    if (nextContent == "}" || nextContent.StartsWith("["))
                    {
                        break; // End of property without semicolon
                    }
                    
                    if (!string.IsNullOrWhiteSpace(nextContent))
                    {
                        propertyText += " " + nextContent;
                        if (!string.IsNullOrEmpty(lines[index].Comment))
                        {
                            propertyComment += " " + lines[index].Comment;
                        }
                    }
                    index++;
                }

                // Parse the property
                var parts = propertyText.Split('=', 2);
                if (parts.Length == 2)
                {
                    var property = new Property
                    {
                        Key = parts[0].Trim(),
                        Value = parts[1].Trim().TrimEnd(';').Trim(),
                        Comment = propertyComment ?? string.Empty
                    };
                    block.Properties.Add(property);
                }
                continue; // Already advanced index
            }
            else
            {
                // Unknown content - just save comment if present
                if (!string.IsNullOrEmpty(comment))
                {
                    block.Comments = AppendComment(block.Comments, comment);
                }
            }
            
            index++;
        }

        // Skip closing brace
        if (index < lines.Count && lines[index].Content == "}")
        {
            if (!string.IsNullOrEmpty(lines[index].Comment))
            {
                block.Comments = AppendComment(block.Comments, lines[index].Comment);
            }
            index++;
        }

        return block;
    }

    public void Write(TaFile taFile, string filePath)
    {
        var content = new StringBuilder();

        if (!string.IsNullOrEmpty(taFile.HeaderComments))
        {
            var lines = taFile.HeaderComments.Split(Environment.NewLine);
            foreach (var line in lines)
            {
                content.AppendLine($"/* {line} */");
            }
            content.AppendLine();
        }

        foreach (var block in taFile.Blocks)
        {
            WriteBlock(content, block, 0);
        }

        File.WriteAllText(filePath, content.ToString(), Encoding.Latin1);
    }

    private void WriteBlock(StringBuilder sb, Block block, int indentLevel)
    {
        var indent = new string('\t', indentLevel);

        // Section header
        sb.AppendLine($"{indent}[{block.SectionName}]");
        
        // Opening brace
        sb.AppendLine($"{indent}\t{{");

        // Standalone comments
        if (!string.IsNullOrEmpty(block.Comments))
        {
            foreach (var line in block.Comments.Split(Environment.NewLine))
            {
                sb.AppendLine($"{indent}\t\t/* {line} */");
            }
        }

        // Properties
        foreach (var prop in block.Properties)
        {
            if (!string.IsNullOrEmpty(prop.Comment))
            {
                sb.AppendLine($"{indent}\t\t{prop.Key}={prop.Value};\t\t/* {prop.Comment} */");
            }
            else
            {
                sb.AppendLine($"{indent}\t\t{prop.Key}={prop.Value};");
            }
        }

        // Blank line before nested blocks
        if (block.Blocks.Count > 0)
        {
            sb.AppendLine();
        }

        // Nested blocks
        foreach (var nestedBlock in block.Blocks)
        {
            WriteBlock(sb, nestedBlock, indentLevel + 1);
        }

        // Closing brace
        sb.AppendLine($"{indent}\t}}");
    }
}