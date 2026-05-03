using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JiraBridge.Infrastructure.Jira;

public static partial class JiraAdfBuilder
{
  public static object BuildDocument(string markdown)
  {
    var content = new List<object>();
    string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    int index = 0;
    while (index < lines.Length)
    {
      string line = lines[index].TrimEnd();
      string trimmed = line.Trim();

      if (string.IsNullOrWhiteSpace(trimmed))
      {
        index++;
        continue;
      }

      if (TryParseHeading(trimmed, out int level, out string headingText))
      {
        content.Add(new
        {
          type = "heading",
          attrs = new { level },
          content = BuildInlineContent(headingText)
        });
        index++;
        continue;
      }

      if (TryParseTable(lines, ref index, out object? tableNode))
      {
        content.Add(tableNode!);
        continue;
      }

      if (trimmed.StartsWith("- ", StringComparison.Ordinal))
      {
        var items = new List<object>();
        while (index < lines.Length)
        {
          string bulletLine = lines[index].Trim();
          if (!bulletLine.StartsWith("- ", StringComparison.Ordinal))
          {
            break;
          }

          string bulletText = bulletLine[2..].Trim();
          items.Add(new
          {
            type = "listItem",
            content = new object[]
            {
              new
              {
                type = "paragraph",
                content = BuildInlineContent(bulletText)
              }
            }
          });
          index++;
        }

        content.Add(new
        {
          type = "bulletList",
          content = items
        });
        continue;
      }

      var paragraphLines = new List<string>();
      while (index < lines.Length)
      {
        string paragraphLine = lines[index].TrimEnd();
        string paragraphTrimmed = paragraphLine.Trim();

        if (string.IsNullOrWhiteSpace(paragraphTrimmed) ||
            TryParseHeading(paragraphTrimmed, out _, out _) ||
            paragraphTrimmed.StartsWith("- ", StringComparison.Ordinal) ||
            IsPotentialTableLine(paragraphTrimmed))
        {
          break;
        }

        paragraphLines.Add(paragraphTrimmed);
        index++;
      }

      content.Add(new
      {
        type = "paragraph",
        content = BuildParagraphContent(paragraphLines)
      });
    }

    return new
    {
      type = "doc",
      version = 1,
      content
    };
  }

  public static string ExtractPlainText(string? adfJson)
  {
    if (string.IsNullOrWhiteSpace(adfJson))
    {
      return string.Empty;
    }

    try
    {
      using var document = JsonDocument.Parse(adfJson);
      var parts = new List<string>();
      TraversePlainText(document.RootElement, parts);
      return string.Join(System.Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
    }
    catch
    {
      return string.Empty;
    }
  }

  public static string ExtractMarkdown(string? adfJson)
  {
    if (string.IsNullOrWhiteSpace(adfJson))
    {
      return string.Empty;
    }

    try
    {
      using var document = JsonDocument.Parse(adfJson);
      var builder = new StringBuilder();
      RenderBlockNode(document.RootElement, builder, 0);
      return builder.ToString().Trim();
    }
    catch
    {
      return string.Empty;
    }
  }

  private static void RenderBlockNode(JsonElement element, StringBuilder builder, int indentLevel)
  {
    if (element.ValueKind != JsonValueKind.Object)
    {
      return;
    }

    string? type = GetType(element);
    switch (type)
    {
      case "doc":
        RenderContentArray(element, builder, indentLevel);
        break;
      case "paragraph":
        AppendParagraph(builder, RenderInlineContent(element), indentLevel);
        break;
      case "heading":
        int level = GetHeadingLevel(element);
        AppendBlock(builder, $"{new string('#', level)} {RenderInlineContent(element)}");
        break;
      case "bulletList":
        RenderList(element, builder, indentLevel, ordered: false);
        break;
      case "orderedList":
        RenderList(element, builder, indentLevel, ordered: true);
        break;
      case "codeBlock":
        AppendCodeBlock(builder, element);
        break;
      case "blockquote":
        AppendBlockQuote(builder, element, indentLevel);
        break;
      case "rule":
        AppendBlock(builder, "---");
        break;
      case "table":
        AppendTable(builder, element);
        break;
      case "panel":
        AppendPanel(builder, element, indentLevel);
        break;
      default:
        RenderContentArray(element, builder, indentLevel);
        break;
    }
  }

  private static void RenderContentArray(JsonElement element, StringBuilder builder, int indentLevel)
  {
    if (!element.TryGetProperty("content", out JsonElement contentElement) ||
        contentElement.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    foreach (JsonElement child in contentElement.EnumerateArray())
    {
      RenderBlockNode(child, builder, indentLevel);
    }
  }

  private static void RenderList(JsonElement listElement, StringBuilder builder, int indentLevel, bool ordered)
  {
    if (!listElement.TryGetProperty("content", out JsonElement contentElement) ||
        contentElement.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    int itemIndex = 1;
    foreach (JsonElement item in contentElement.EnumerateArray())
    {
      if (!string.Equals(GetType(item), "listItem", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string marker = ordered ? $"{itemIndex}." : "-";
      AppendListItem(builder, item, indentLevel, marker);
      itemIndex++;
    }

    EnsureBlankLine(builder);
  }

  private static void AppendListItem(StringBuilder builder, JsonElement itemElement, int indentLevel, string marker)
  {
    string indent = new(' ', indentLevel * 2);
    string continuationIndent = indent + "  ";

    if (!itemElement.TryGetProperty("content", out JsonElement contentElement) ||
        contentElement.ValueKind != JsonValueKind.Array)
    {
      builder.AppendLine($"{indent}{marker}");
      return;
    }

    bool firstLineWritten = false;
    foreach (JsonElement child in contentElement.EnumerateArray())
    {
      string? childType = GetType(child);
      if (string.Equals(childType, "paragraph", StringComparison.OrdinalIgnoreCase))
      {
        string text = RenderInlineContent(child);
        if (!firstLineWritten)
        {
          builder.AppendLine($"{indent}{marker} {text}");
          firstLineWritten = true;
        }
        else
        {
          foreach (string line in SplitLines(text))
          {
            builder.AppendLine($"{continuationIndent}{line}");
          }
        }

        continue;
      }

      if (!firstLineWritten)
      {
        builder.AppendLine($"{indent}{marker}");
        firstLineWritten = true;
      }

      var nestedBuilder = new StringBuilder();
      RenderBlockNode(child, nestedBuilder, indentLevel + 1);
      string nestedText = nestedBuilder.ToString().TrimEnd();
      if (string.IsNullOrWhiteSpace(nestedText))
      {
        continue;
      }

      foreach (string line in SplitLines(nestedText))
      {
        builder.AppendLine($"{continuationIndent}{line}");
      }
    }

    if (!firstLineWritten)
    {
      builder.AppendLine($"{indent}{marker}");
    }
  }

  private static void AppendParagraph(StringBuilder builder, string text, int indentLevel)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return;
    }

    string indent = new(' ', indentLevel * 2);
    if (indentLevel == 0)
    {
      AppendBlock(builder, text);
      return;
    }

    foreach (string line in SplitLines(text))
    {
      builder.AppendLine($"{indent}{line}");
    }

    builder.AppendLine();
  }

  private static void AppendCodeBlock(StringBuilder builder, JsonElement element)
  {
    string language = element.TryGetProperty("attrs", out JsonElement attrsElement) &&
                      attrsElement.TryGetProperty("language", out JsonElement languageElement)
      ? languageElement.GetString() ?? string.Empty
      : string.Empty;

    string content = RenderInlineContent(element);
    builder.AppendLine($"```{language}".TrimEnd());
    if (!string.IsNullOrEmpty(content))
    {
      builder.AppendLine(content);
    }

    builder.AppendLine("```");
    builder.AppendLine();
  }

  private static void AppendBlockQuote(StringBuilder builder, JsonElement element, int indentLevel)
  {
    var nestedBuilder = new StringBuilder();
    RenderContentArray(element, nestedBuilder, indentLevel);
    string quoteText = nestedBuilder.ToString().Trim();
    if (string.IsNullOrWhiteSpace(quoteText))
    {
      return;
    }

    foreach (string line in SplitLines(quoteText))
    {
      builder.AppendLine($"> {line}");
    }

    builder.AppendLine();
  }

  private static void AppendPanel(StringBuilder builder, JsonElement element, int indentLevel)
  {
    var nestedBuilder = new StringBuilder();
    RenderContentArray(element, nestedBuilder, indentLevel);
    string panelText = nestedBuilder.ToString().Trim();
    if (string.IsNullOrWhiteSpace(panelText))
    {
      return;
    }

    foreach (string line in SplitLines(panelText))
    {
      builder.AppendLine($"> {line}");
    }

    builder.AppendLine();
  }

  private static void AppendTable(StringBuilder builder, JsonElement tableElement)
  {
    if (!tableElement.TryGetProperty("content", out JsonElement rowsElement) ||
        rowsElement.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    var rows = new List<List<string>>();
    foreach (JsonElement rowElement in rowsElement.EnumerateArray())
    {
      if (!rowElement.TryGetProperty("content", out JsonElement cellsElement) ||
          cellsElement.ValueKind != JsonValueKind.Array)
      {
        continue;
      }

      var cells = new List<string>();
      foreach (JsonElement cellElement in cellsElement.EnumerateArray())
      {
        cells.Add(RenderInlineContent(cellElement).Replace(System.Environment.NewLine, " ", StringComparison.Ordinal).Trim());
      }

      rows.Add(cells);
    }

    if (rows.Count == 0)
    {
      return;
    }

    List<string> headerCells = rows[0];
    builder.AppendLine($"| {string.Join(" | ", headerCells)} |");
    builder.AppendLine($"| {string.Join(" | ", headerCells.Select(_ => "---"))} |");

    foreach (List<string> row in rows.Skip(1))
    {
      builder.AppendLine($"| {string.Join(" | ", row)} |");
    }

    builder.AppendLine();
  }

  private static string RenderInlineContent(JsonElement element)
  {
    if (!element.TryGetProperty("content", out JsonElement contentElement) ||
        contentElement.ValueKind != JsonValueKind.Array)
    {
      if (string.Equals(GetType(element), "text", StringComparison.OrdinalIgnoreCase) &&
          element.TryGetProperty("text", out JsonElement textElement))
      {
        return ApplyMarks(textElement.GetString() ?? string.Empty, element);
      }

      return string.Empty;
    }

    var builder = new StringBuilder();
    foreach (JsonElement child in contentElement.EnumerateArray())
    {
      builder.Append(RenderInlineNode(child));
    }

    return builder.ToString().TrimEnd();
  }

  private static string RenderInlineNode(JsonElement element)
  {
    string? type = GetType(element);
    return type switch
    {
      "text" => ApplyMarks(element.TryGetProperty("text", out JsonElement textElement) ? textElement.GetString() ?? string.Empty : string.Empty, element),
      "hardBreak" => System.Environment.NewLine,
      "inlineCard" => element.TryGetProperty("attrs", out JsonElement attrsElement) &&
                      attrsElement.TryGetProperty("url", out JsonElement urlElement)
        ? $"<{urlElement.GetString()}>"
        : string.Empty,
      "emoji" => element.TryGetProperty("attrs", out JsonElement emojiAttrs) &&
                 emojiAttrs.TryGetProperty("text", out JsonElement emojiText)
        ? emojiText.GetString() ?? string.Empty
        : string.Empty,
      "mention" => element.TryGetProperty("attrs", out JsonElement mentionAttrs) &&
                   mentionAttrs.TryGetProperty("text", out JsonElement mentionText)
        ? mentionText.GetString() ?? string.Empty
        : string.Empty,
      _ => RenderInlineContent(element)
    };
  }

  private static string ApplyMarks(string text, JsonElement element)
  {
    if (!element.TryGetProperty("marks", out JsonElement marksElement) ||
        marksElement.ValueKind != JsonValueKind.Array)
    {
      return text;
    }

    string result = text;
    bool isCode = false;
    bool isStrong = false;
    bool isEmphasis = false;
    bool isStrike = false;
    string? link = null;

    foreach (JsonElement markElement in marksElement.EnumerateArray())
    {
      if (!markElement.TryGetProperty("type", out JsonElement markTypeElement))
      {
        continue;
      }

      string? markType = markTypeElement.GetString();
      switch (markType)
      {
        case "code":
          isCode = true;
          break;
        case "strong":
          isStrong = true;
          break;
        case "em":
          isEmphasis = true;
          break;
        case "strike":
          isStrike = true;
          break;
        case "link":
          if (markElement.TryGetProperty("attrs", out JsonElement attrsElement) &&
              attrsElement.TryGetProperty("href", out JsonElement hrefElement))
          {
            link = hrefElement.GetString();
          }
          break;
      }
    }

    if (isCode)
    {
      result = $"`{result}`";
    }

    if (isStrong)
    {
      result = $"**{result}**";
    }

    if (isEmphasis)
    {
      result = $"*{result}*";
    }

    if (isStrike)
    {
      result = $"~~{result}~~";
    }

    if (!string.IsNullOrWhiteSpace(link))
    {
      result = $"[{result}]({link})";
    }

    return result;
  }

  private static List<object> BuildParagraphContent(List<string> lines)
  {
    var content = new List<object>();

    for (int i = 0; i < lines.Count; i++)
    {
      if (i > 0)
      {
        content.Add(new { type = "hardBreak" });
      }

      content.AddRange(BuildInlineContent(lines[i]));
    }

    return content;
  }

  private static List<object> BuildInlineContent(string text)
  {
    var content = new List<object>();
    int currentIndex = 0;

    foreach (Match match in BoldRegex().Matches(text))
    {
      if (match.Index > currentIndex)
      {
        content.Add(new
        {
          type = "text",
          text = text[currentIndex..match.Index]
        });
      }

      string boldText = match.Groups[1].Value;
      if (!string.IsNullOrEmpty(boldText))
      {
        content.Add(new
        {
          type = "text",
          text = boldText,
          marks = new object[]
          {
            new { type = "strong" }
          }
        });
      }

      currentIndex = match.Index + match.Length;
    }

    if (currentIndex < text.Length)
    {
      content.Add(new
      {
        type = "text",
        text = text[currentIndex..]
      });
    }

    if (content.Count == 0)
    {
      content.Add(new
      {
        type = "text",
        text
      });
    }

    return content;
  }

  private static bool TryParseTable(string[] lines, ref int index, out object? tableNode)
  {
    tableNode = null;

    if (index + 1 >= lines.Length)
    {
      return false;
    }

    string headerLine = lines[index].Trim();
    string separatorLine = lines[index + 1].Trim();
    if (!IsPotentialTableLine(headerLine) || !TableSeparatorRegex().IsMatch(separatorLine))
    {
      return false;
    }

    var rows = new List<string> { headerLine };
    index += 2;

    while (index < lines.Length)
    {
      string current = lines[index].Trim();
      if (!IsPotentialTableLine(current))
      {
        break;
      }

      rows.Add(current);
      index++;
    }

    string[] headerCells = SplitTableRow(headerLine);
    var tableRows = new List<object>
    {
      new
      {
        type = "tableRow",
        content = headerCells.Select(cell => new
        {
          type = "tableHeader",
          content = new object[]
          {
            new
            {
              type = "paragraph",
              content = BuildInlineContent(cell)
            }
          }
        }).ToArray()
      }
    };

    foreach (string row in rows.Skip(1))
    {
      string[] cells = SplitTableRow(row);
      tableRows.Add(new
      {
        type = "tableRow",
        content = cells.Select(cell => new
        {
          type = "tableCell",
          content = new object[]
          {
            new
            {
              type = "paragraph",
              content = BuildInlineContent(cell)
            }
          }
        }).ToArray()
      });
    }

    tableNode = new
    {
      type = "table",
      attrs = new { isNumberColumnEnabled = false, layout = "default" },
      content = tableRows
    };

    return true;
  }

  private static string[] SplitTableRow(string row)
  {
    string trimmed = row.Trim().Trim('|');
    return trimmed.Split('|').Select(cell => cell.Trim()).ToArray();
  }

  private static bool IsPotentialTableLine(string line) =>
    line.Contains('|', StringComparison.Ordinal) &&
    line.Trim().StartsWith("|", StringComparison.Ordinal) &&
    line.Trim().EndsWith("|", StringComparison.Ordinal);

  private static bool TryParseHeading(string line, out int level, out string text)
  {
    level = 0;
    text = string.Empty;

    int count = 0;
    while (count < line.Length && line[count] == '#')
    {
      count++;
    }

    if (count is < 1 or > 6 || count >= line.Length || line[count] != ' ')
    {
      return false;
    }

    level = count;
    text = line[(count + 1)..].Trim();
    return true;
  }

  private static void TraversePlainText(JsonElement element, List<string> parts)
  {
    if (element.ValueKind == JsonValueKind.Object)
    {
      if (element.TryGetProperty("type", out JsonElement typeElement) &&
          typeElement.ValueKind == JsonValueKind.String)
      {
        string? type = typeElement.GetString();
        if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase) &&
            element.TryGetProperty("text", out JsonElement textElement))
        {
          parts.Add(textElement.GetString() ?? string.Empty);
        }
        else if (string.Equals(type, "hardBreak", StringComparison.OrdinalIgnoreCase))
        {
          parts.Add(System.Environment.NewLine);
        }
      }

      foreach (JsonProperty property in element.EnumerateObject())
      {
        TraversePlainText(property.Value, parts);
      }

      return;
    }

    if (element.ValueKind == JsonValueKind.Array)
    {
      foreach (JsonElement item in element.EnumerateArray())
      {
        TraversePlainText(item, parts);
      }
    }
  }

  private static string? GetType(JsonElement element) =>
    element.TryGetProperty("type", out JsonElement typeElement) &&
    typeElement.ValueKind == JsonValueKind.String
      ? typeElement.GetString()
      : null;

  private static int GetHeadingLevel(JsonElement element)
  {
    if (element.TryGetProperty("attrs", out JsonElement attrsElement) &&
        attrsElement.TryGetProperty("level", out JsonElement levelElement) &&
        levelElement.TryGetInt32(out int level))
    {
      return Math.Clamp(level, 1, 6);
    }

    return 2;
  }

  private static void AppendBlock(StringBuilder builder, string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return;
    }

    builder.AppendLine(text.TrimEnd());
    builder.AppendLine();
  }

  private static void EnsureBlankLine(StringBuilder builder)
  {
    if (builder.Length == 0)
    {
      return;
    }

    if (!builder.ToString().EndsWith(System.Environment.NewLine + System.Environment.NewLine, StringComparison.Ordinal))
    {
      builder.AppendLine();
    }
  }

  private static IEnumerable<string> SplitLines(string value) =>
    value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

  [GeneratedRegex(@"\*\*(.+?)\*\*")]
  private static partial Regex BoldRegex();

  [GeneratedRegex(@"^\|?[\s:\-]+(\|[\s:\-]+)+\|?$")]
  private static partial Regex TableSeparatorRegex();
}
