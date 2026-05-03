using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JiraBridge.Infrastructure.Jira;

public static partial class JiraAdfBuilder
{
  public static object BuildDocument(string markdown, bool useNativeTaskLists = true)
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

      if (IsHorizontalRuleLine(trimmed))
      {
        content.Add(new { type = "rule" });
        index++;
        continue;
      }

      if (trimmed.StartsWith("- ", StringComparison.Ordinal))
      {
        if (useNativeTaskLists && TryParseTaskList(lines, ref index, out object? taskListNode))
        {
          content.Add(taskListNode!);
          continue;
        }

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

      if (TryParseOrderedList(lines, ref index, out object? orderedListNode))
      {
        content.Add(orderedListNode!);
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
            OrderedListLineRegex().IsMatch(paragraphTrimmed) ||
            IsHorizontalRuleLine(paragraphTrimmed) ||
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
      case "taskList":
        RenderTaskList(element, builder, indentLevel);
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
      case "expand":
      case "nestedExpand":
        RenderExpandLike(element, builder, indentLevel, string.Equals(type, "nestedExpand", StringComparison.OrdinalIgnoreCase)
          ? "Nested expand"
          : "Expand");
        break;
      case "layoutSection":
        RenderLayoutSection(element, builder, indentLevel);
        break;
      case "layoutColumn":
        RenderContentArray(element, builder, indentLevel);
        break;
      case "decisionList":
        RenderDecisionList(element, builder, indentLevel);
        break;
      case "mediaSingle":
      case "mediaGroup":
        RenderMediaContainer(element, builder, indentLevel);
        break;
      case "media":
        AppendMediaLine(builder, element, indentLevel);
        EnsureBlankLine(builder);
        break;
      case "extension":
        AppendExtensionSummary(builder, element, indentLevel);
        break;
      case "bodiedExtension":
        AppendExtensionSummary(builder, element, indentLevel);
        RenderContentArray(element, builder, indentLevel);
        break;
      case "embedCard":
      case "blockCard":
        AppendCardSummary(builder, element, indentLevel);
        break;
      case "caption":
        RenderContentArray(element, builder, indentLevel);
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
    if (ordered &&
        listElement.TryGetProperty("attrs", out JsonElement attrsElement) &&
        attrsElement.TryGetProperty("order", out JsonElement orderElement) &&
        orderElement.TryGetInt32(out int startOrder))
    {
      itemIndex = startOrder;
    }

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

  private static void RenderTaskList(JsonElement listElement, StringBuilder builder, int indentLevel)
  {
    if (!listElement.TryGetProperty("content", out JsonElement contentElement) ||
        contentElement.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    string indent = new string(' ', indentLevel * 2);
    foreach (JsonElement item in contentElement.EnumerateArray())
    {
      if (!string.Equals(GetType(item), "taskItem", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string prefix = TaskItemMarkdownPrefix(item);
      string body = RenderTaskItemContent(item);
      string normalizedBody = body.Replace("\r\n", "\n", StringComparison.Ordinal);
      string[] bodyLines = SplitLines(normalizedBody).ToArray();

      if (bodyLines.Length == 0 || bodyLines.All(string.IsNullOrWhiteSpace))
      {
        builder.AppendLine($"{indent}{prefix.TrimEnd()}");
        continue;
      }

      builder.AppendLine($"{indent}{prefix}{bodyLines[0]}");
      string continuation = indent + new string(' ', prefix.Length);
      for (int i = 1; i < bodyLines.Length; i++)
      {
        builder.AppendLine($"{continuation}{bodyLines[i]}");
      }
    }

    EnsureBlankLine(builder);
  }

  private static string TaskItemMarkdownPrefix(JsonElement taskItem)
  {
    bool done = false;
    if (taskItem.TryGetProperty("attrs", out JsonElement attrs) &&
        attrs.TryGetProperty("state", out JsonElement stateEl))
    {
      string? state = stateEl.GetString();
      if (!string.IsNullOrWhiteSpace(state))
      {
        done = state.Equals("DONE", StringComparison.OrdinalIgnoreCase) ||
               state.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase);
      }
    }

    return done ? "- [x] " : "- [ ] ";
  }

  private static string RenderTaskItemContent(JsonElement taskItem)
  {
    if (!taskItem.TryGetProperty("content", out JsonElement contentElement) ||
        contentElement.ValueKind != JsonValueKind.Array)
    {
      return CollectTaskItemPlainTextFallback(taskItem);
    }

    var sb = new StringBuilder();
    foreach (JsonElement child in contentElement.EnumerateArray())
    {
      string? childType = GetType(child);
      if (string.Equals(childType, "paragraph", StringComparison.OrdinalIgnoreCase))
      {
        if (sb.Length > 0)
        {
          sb.AppendLine();
        }

        sb.Append(RenderInlineContent(child));
      }
      else if (string.Equals(childType, "text", StringComparison.OrdinalIgnoreCase))
      {
        if (sb.Length > 0)
        {
          sb.AppendLine();
        }

        sb.Append(RenderInlineNode(child));
      }
      else
      {
        var nested = new StringBuilder();
        RenderBlockNode(child, nested, 0);
        string block = nested.ToString().TrimEnd();
        if (sb.Length > 0 && block.Length > 0)
        {
          sb.AppendLine();
        }

        sb.Append(block);
      }
    }

    string primary = sb.ToString().TrimEnd();
    return string.IsNullOrWhiteSpace(primary) ? CollectTaskItemPlainTextFallback(taskItem) : primary;
  }

  private static string CollectTaskItemPlainTextFallback(JsonElement taskItem)
  {
    var parts = new List<string>();
    AppendTaskItemTextNodes(taskItem, parts);
    return string.Concat(parts).Trim();
  }

  private static void AppendTaskItemTextNodes(JsonElement element, List<string> parts)
  {
    switch (element.ValueKind)
    {
      case JsonValueKind.Object:
        string? type = GetType(element);
        if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase) &&
            element.TryGetProperty("text", out JsonElement textEl))
        {
          parts.Add(textEl.GetString() ?? string.Empty);
          return;
        }

        if (string.Equals(type, "hardBreak", StringComparison.OrdinalIgnoreCase))
        {
          parts.Add(System.Environment.NewLine);
          return;
        }

        if (string.Equals(type, "mention", StringComparison.OrdinalIgnoreCase) &&
            element.TryGetProperty("attrs", out JsonElement mentionAttrs) &&
            mentionAttrs.TryGetProperty("text", out JsonElement mentionText))
        {
          parts.Add(mentionText.GetString() ?? string.Empty);
          return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
          AppendTaskItemTextNodes(property.Value, parts);
        }

        break;
      case JsonValueKind.Array:
        foreach (JsonElement item in element.EnumerateArray())
        {
          AppendTaskItemTextNodes(item, parts);
        }

        break;
      default:
        break;
    }
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
        : "[inline card]",
      "emoji" => element.TryGetProperty("attrs", out JsonElement emojiAttrs) &&
                 emojiAttrs.TryGetProperty("text", out JsonElement emojiText)
        ? emojiText.GetString() ?? string.Empty
        : string.Empty,
      "mention" => element.TryGetProperty("attrs", out JsonElement mentionAttrs) &&
                   mentionAttrs.TryGetProperty("text", out JsonElement mentionText)
        ? mentionText.GetString() ?? string.Empty
        : string.Empty,
      "date" => FormatAdfDateAttr(element),
      "status" => element.TryGetProperty("attrs", out JsonElement statusAttrs) &&
                  statusAttrs.TryGetProperty("text", out JsonElement statusText)
        ? $"[{statusText.GetString()}]"
        : "[status]",
      "placeholder" => element.TryGetProperty("attrs", out JsonElement phAttrs) &&
                         phAttrs.TryGetProperty("text", out JsonElement phText)
        ? $"{{{phText.GetString()}}}"
        : "{placeholder}",
      "inlineExtension" => RenderExtensionAttrsPlain(element),
      "mediaInline" => FormatMediaReference(element),
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
    bool isUnderline = false;
    string? subSupKind = null;
    string? textColorHex = null;
    string? backgroundColorHex = null;
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
        case "annotation":
        case "border":
        case "breakout":
        case "dataConsumer":
        case "fragment":
          break;
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
        case "underline":
          isUnderline = true;
          break;
        case "subsup":
          if (markElement.TryGetProperty("attrs", out JsonElement subAttrs) &&
              subAttrs.TryGetProperty("type", out JsonElement subTypeEl))
          {
            subSupKind = subTypeEl.GetString();
          }

          break;
        case "textColor":
          if (markElement.TryGetProperty("attrs", out JsonElement tcAttrs) &&
              tcAttrs.TryGetProperty("color", out JsonElement colorEl))
          {
            textColorHex = colorEl.GetString();
          }

          break;
        case "backgroundColor":
          if (markElement.TryGetProperty("attrs", out JsonElement bgAttrs) &&
              bgAttrs.TryGetProperty("color", out JsonElement bgEl))
          {
            backgroundColorHex = bgEl.GetString();
          }

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

    if (!string.IsNullOrEmpty(subSupKind))
    {
      result = string.Equals(subSupKind, "sup", StringComparison.OrdinalIgnoreCase)
        ? $"<sup>{result}</sup>"
        : $"<sub>{result}</sub>";
    }

    if (isUnderline)
    {
      result = $"<u>{result}</u>";
    }

    if (!string.IsNullOrWhiteSpace(textColorHex))
    {
      result = $"<span style=\"color:{textColorHex}\">{result}</span>";
    }

    if (!string.IsNullOrWhiteSpace(backgroundColorHex))
    {
      result = $"<span style=\"background-color:{backgroundColorHex}\">{result}</span>";
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
    var sink = new List<object>();
    if (string.IsNullOrEmpty(text))
    {
      sink.Add(new { type = "text", text = string.Empty });
      return sink;
    }

    int pos = 0;
    while (pos < text.Length)
    {
      if (TryConsumeInlineCode(text, ref pos, sink) ||
          TryConsumeMarkdownLink(text, ref pos, sink) ||
          TryConsumeBold(text, ref pos, sink) ||
          TryConsumeStrike(text, ref pos, sink) ||
          TryConsumeItalicStar(text, ref pos, sink) ||
          TryConsumeItalicUnderscore(text, ref pos, sink))
      {
        continue;
      }

      int next = FindNextInlineSpecial(text, pos + 1);
      ReadOnlySpan<char> span = text.AsSpan();
      string chunk = next >= 0 ? span[pos..next].ToString() : span[pos..].ToString();
      if (chunk.Length > 0)
      {
        sink.Add(new { type = "text", text = chunk });
      }

      pos = next >= 0 ? next : text.Length;
    }

    MergeAdjacentPlainTextNodes(sink);
    return sink.Count == 0 ? [new { type = "text", text = string.Empty }] : sink;
  }

  private static int FindNextInlineSpecial(string text, int start)
  {
    for (int i = start; i < text.Length; i++)
    {
      char c = text[i];
      if (c is '`' or '[' or '*' or '~' or '_')
      {
        return i;
      }
    }

    return -1;
  }

  private static bool TryConsumeInlineCode(string text, ref int pos, List<object> sink)
  {
    if (text[pos] != '`')
    {
      return false;
    }

    int close = text.IndexOf('`', pos + 1);
    if (close < 0)
    {
      return false;
    }

    string code = text.Substring(pos + 1, close - pos - 1);
    sink.Add(new
    {
      type = "text",
      text = code,
      marks = new object[] { new { type = "code" } }
    });
    pos = close + 1;
    return true;
  }

  private static bool TryConsumeMarkdownLink(string text, ref int pos, List<object> sink)
  {
    if (text[pos] != '[')
    {
      return false;
    }

    int bracketClose = FindClosingBracket(text, pos + 1);
    if (bracketClose < 0 ||
        bracketClose + 1 >= text.Length ||
        text[bracketClose + 1] != '(')
    {
      return false;
    }

    int parenClose = text.IndexOf(')', bracketClose + 2);
    if (parenClose < 0)
    {
      return false;
    }

    string label = text.Substring(pos + 1, bracketClose - pos - 1);
    string url = text.Substring(bracketClose + 2, parenClose - bracketClose - 2).Trim();
    List<object> labelNodes = BuildInlineContent(label);
    sink.AddRange(ApplyMarkToTextNodes(labelNodes, new { type = "link", attrs = new { href = url } }));
    pos = parenClose + 1;
    return true;
  }

  private static int FindClosingBracket(string text, int start)
  {
    int depth = 1;
    for (int i = start; i < text.Length; i++)
    {
      switch (text[i])
      {
        case '[':
          depth++;
          break;
        case ']':
          depth--;
          if (depth == 0)
          {
            return i;
          }

          break;
      }
    }

    return -1;
  }

  private static bool TryConsumeBold(string text, ref int pos, List<object> sink)
  {
    if (pos + 1 >= text.Length || text[pos] != '*' || text[pos + 1] != '*')
    {
      return false;
    }

    int close = text.IndexOf("**", pos + 2, StringComparison.Ordinal);
    if (close < 0)
    {
      return false;
    }

    string inner = text.Substring(pos + 2, close - pos - 2);
    sink.AddRange(ApplyMarkToTextNodes(BuildInlineContent(inner), new { type = "strong" }));
    pos = close + 2;
    return true;
  }

  private static bool TryConsumeStrike(string text, ref int pos, List<object> sink)
  {
    if (pos + 1 >= text.Length || text[pos] != '~' || text[pos + 1] != '~')
    {
      return false;
    }

    int close = text.IndexOf("~~", pos + 2, StringComparison.Ordinal);
    if (close < 0)
    {
      return false;
    }

    string inner = text.Substring(pos + 2, close - pos - 2);
    sink.AddRange(ApplyMarkToTextNodes(BuildInlineContent(inner), new { type = "strike" }));
    pos = close + 2;
    return true;
  }

  private static bool TryConsumeItalicStar(string text, ref int pos, List<object> sink)
  {
    if (text[pos] != '*' || (pos + 1 < text.Length && text[pos + 1] == '*'))
    {
      return false;
    }

    int close = -1;
    for (int j = pos + 1; j < text.Length; j++)
    {
      if (text[j] != '*')
      {
        continue;
      }

      if (j + 1 < text.Length && text[j + 1] == '*')
      {
        continue;
      }

      close = j;
      break;
    }

    if (close <= pos + 1)
    {
      return false;
    }

    string inner = text.Substring(pos + 1, close - pos - 1);
    sink.AddRange(ApplyMarkToTextNodes(BuildInlineContent(inner), new { type = "em" }));
    pos = close + 1;
    return true;
  }

  private static bool TryConsumeItalicUnderscore(string text, ref int pos, List<object> sink)
  {
    if (text[pos] != '_' || (pos + 1 < text.Length && text[pos + 1] == '_'))
    {
      return false;
    }

    int close = text.IndexOf('_', pos + 1);
    if (close <= pos + 1)
    {
      return false;
    }

    string inner = text.Substring(pos + 1, close - pos - 1);
    if (inner.Contains('_', StringComparison.Ordinal))
    {
      return false;
    }

    sink.AddRange(ApplyMarkToTextNodes(BuildInlineContent(inner), new { type = "em" }));
    pos = close + 1;
    return true;
  }

  private static List<object> ApplyMarkToTextNodes(List<object> nodes, object markToAdd)
  {
    string markJson = JsonSerializer.Serialize(markToAdd);
    object deserializedMark = JsonSerializer.Deserialize<object>(markJson)!;
    var result = new List<object>();
    foreach (object node in nodes)
    {
      using JsonDocument nodeDoc = JsonDocument.Parse(JsonSerializer.Serialize(node));
      JsonElement root = nodeDoc.RootElement;
      string? nodeType = root.GetProperty("type").GetString();
      if (!string.Equals(nodeType, "text", StringComparison.Ordinal))
      {
        result.Add(node);
        continue;
      }

      string nodeText = root.GetProperty("text").GetString() ?? string.Empty;
      var marks = new List<object>();
      if (root.TryGetProperty("marks", out JsonElement existingMarks))
      {
        foreach (JsonElement m in existingMarks.EnumerateArray())
        {
          marks.Add(JsonSerializer.Deserialize<object>(m.GetRawText())!);
        }
      }

      marks.Add(deserializedMark);
      result.Add(new { type = "text", text = nodeText, marks = marks.ToArray() });
    }

    return result;
  }

  private static void MergeAdjacentPlainTextNodes(List<object> nodes)
  {
    int i = 0;
    while (i < nodes.Count - 1)
    {
      if (!TryGetPlainTextNode(nodes[i], out string? a) || !TryGetPlainTextNode(nodes[i + 1], out string? b))
      {
        i++;
        continue;
      }

      nodes[i] = new { type = "text", text = a + b };
      nodes.RemoveAt(i + 1);
    }
  }

  private static bool TryGetPlainTextNode(object node, out string text)
  {
    text = string.Empty;
    using JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(node));
    JsonElement root = doc.RootElement;
    if (!string.Equals(root.GetProperty("type").GetString(), "text", StringComparison.Ordinal))
    {
      return false;
    }

    if (root.TryGetProperty("marks", out _))
    {
      return false;
    }

    text = root.GetProperty("text").GetString() ?? string.Empty;
    return true;
  }

  private static bool TryParseOrderedList(string[] lines, ref int index, out object? listNode)
  {
    listNode = null;
    string trimmed = lines[index].Trim();
    Match firstMatch = OrderedListLineRegex().Match(trimmed);
    if (!firstMatch.Success)
    {
      return false;
    }

    int startOrder = int.Parse(firstMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    var items = new List<object>();

    while (index < lines.Length)
    {
      string lineTrim = lines[index].Trim();
      Match m = OrderedListLineRegex().Match(lineTrim);
      if (!m.Success)
      {
        break;
      }

      string itemText = m.Groups[2].Value.Trim();
      items.Add(new
      {
        type = "listItem",
        content = new object[]
        {
          new
          {
            type = "paragraph",
            content = BuildInlineContent(itemText)
          }
        }
      });
      index++;
    }

    listNode = new
    {
      type = "orderedList",
      attrs = new { order = startOrder },
      content = items
    };
    return true;
  }

  private static bool TryParseTaskList(string[] lines, ref int index, out object? taskListNode)
  {
    taskListNode = null;
    string trimmed = lines[index].Trim();
    if (!TaskListItemLineRegex().IsMatch(trimmed))
    {
      return false;
    }

    string listLocalId = Guid.NewGuid().ToString("D");
    var items = new List<object>();

    while (index < lines.Length)
    {
      string lineTrim = lines[index].Trim();
      Match match = TaskListItemLineRegex().Match(lineTrim);
      if (!match.Success)
      {
        break;
      }

      bool done = match.Groups["done"].Success;
      string body = match.Groups["body"].Value.Trim();
      string itemLocalId = Guid.NewGuid().ToString("D");

      items.Add(new
      {
        type = "taskItem",
        attrs = new { localId = itemLocalId, state = done ? "DONE" : "TODO" },
        content = BuildInlineContent(body).ToArray()
      });
      index++;
    }

    taskListNode = new
    {
      type = "taskList",
      attrs = new { localId = listLocalId },
      content = items
    };
    return true;
  }

  private static bool IsHorizontalRuleLine(string trimmed) =>
    HorizontalRuleRegex().IsMatch(trimmed);

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

  private static void RenderExpandLike(JsonElement element, StringBuilder builder, int indentLevel, string kindLabel)
  {
    string title = ExpandTitle(element);
    string header = string.IsNullOrWhiteSpace(title) ? kindLabel : $"{kindLabel}: {title}";
    AppendBlock(builder, header);
    RenderContentArray(element, builder, indentLevel);
  }

  private static string ExpandTitle(JsonElement element)
  {
    if (element.TryGetProperty("attrs", out JsonElement attrs) &&
        attrs.TryGetProperty("title", out JsonElement titleEl))
    {
      return titleEl.GetString() ?? string.Empty;
    }

    return string.Empty;
  }

  private static void RenderLayoutSection(JsonElement element, StringBuilder builder, int indentLevel)
  {
    if (!element.TryGetProperty("content", out JsonElement cols) || cols.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    bool first = true;
    foreach (JsonElement col in cols.EnumerateArray())
    {
      if (!first)
      {
        builder.AppendLine();
      }

      first = false;
      RenderBlockNode(col, builder, indentLevel);
    }

    EnsureBlankLine(builder);
  }

  private static void RenderDecisionList(JsonElement listElement, StringBuilder builder, int indentLevel)
  {
    if (!listElement.TryGetProperty("content", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    string indent = new string(' ', indentLevel * 2);
    foreach (JsonElement item in items.EnumerateArray())
    {
      if (!string.Equals(GetType(item), "decisionItem", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string prefix = DecisionItemMarkdownPrefix(item);
      string body = RenderDecisionItemInlineBody(item);
      builder.AppendLine($"{indent}{prefix}{body}");
    }

    EnsureBlankLine(builder);
  }

  private static string DecisionItemMarkdownPrefix(JsonElement decisionItem)
  {
    string state = string.Empty;
    if (decisionItem.TryGetProperty("attrs", out JsonElement attrs) &&
        attrs.TryGetProperty("state", out JsonElement stateEl))
    {
      state = stateEl.GetString() ?? string.Empty;
    }

    if (state.Equals("DECIDED", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("DONE", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase))
    {
      return "- [x] ";
    }

    return "- [ ] ";
  }

  private static string RenderDecisionItemInlineBody(JsonElement decisionItem)
  {
    if (!decisionItem.TryGetProperty("content", out JsonElement contentElement) ||
        contentElement.ValueKind != JsonValueKind.Array)
    {
      return string.Empty;
    }

    var sb = new StringBuilder();
    foreach (JsonElement child in contentElement.EnumerateArray())
    {
      sb.Append(RenderInlineNode(child));
    }

    return sb.ToString().TrimEnd();
  }

  private static void RenderMediaContainer(JsonElement element, StringBuilder builder, int indentLevel)
  {
    if (!element.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
    {
      AppendBlock(builder, "[media]");
      return;
    }

    foreach (JsonElement child in content.EnumerateArray())
    {
      string? childType = GetType(child);
      if (string.Equals(childType, "media", StringComparison.OrdinalIgnoreCase))
      {
        AppendMediaLine(builder, child, indentLevel);
      }
      else
      {
        RenderBlockNode(child, builder, indentLevel);
      }
    }

    EnsureBlankLine(builder);
  }

  private static void AppendMediaLine(StringBuilder builder, JsonElement mediaElement, int indentLevel)
  {
    string indent = new string(' ', indentLevel * 2);
    builder.AppendLine($"{indent}{FormatMediaReference(mediaElement)}");
  }

  private static string FormatMediaReference(JsonElement mediaElement)
  {
    if (!mediaElement.TryGetProperty("attrs", out JsonElement attrs))
    {
      return "[embedded media]";
    }

    if (attrs.TryGetProperty("type", out JsonElement typeEl))
    {
      string? mediaType = typeEl.GetString();
      if (string.Equals(mediaType, "external", StringComparison.OrdinalIgnoreCase) &&
          attrs.TryGetProperty("url", out JsonElement urlEl))
      {
        string url = urlEl.GetString() ?? string.Empty;
        string alt = attrs.TryGetProperty("alt", out JsonElement altEl) ? altEl.GetString() ?? "media" : "media";
        return $"[{alt}]({url})";
      }

      if ((string.Equals(mediaType, "file", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(mediaType, "link", StringComparison.OrdinalIgnoreCase)) &&
          attrs.TryGetProperty("id", out JsonElement idEl))
      {
        string id = idEl.GetString() ?? string.Empty;
        string alt = attrs.TryGetProperty("alt", out JsonElement altEl2) ? altEl2.GetString() ?? string.Empty : string.Empty;
        string label = string.IsNullOrWhiteSpace(alt) ? id : alt;
        return $"[Embedded attachment: {label}]";
      }
    }

    return "[embedded media]";
  }

  private static void AppendExtensionSummary(StringBuilder builder, JsonElement element, int indentLevel)
  {
    string indent = new string(' ', indentLevel * 2);
    builder.AppendLine($"{indent}{RenderExtensionAttrsPlain(element)}");
  }

  private static void AppendCardSummary(StringBuilder builder, JsonElement element, int indentLevel)
  {
    string indent = new string(' ', indentLevel * 2);
    if (element.TryGetProperty("attrs", out JsonElement attrs) &&
        attrs.TryGetProperty("url", out JsonElement urlEl))
    {
      string url = urlEl.GetString() ?? string.Empty;
      if (!string.IsNullOrWhiteSpace(url))
      {
        builder.AppendLine($"{indent}<{url}>");
        return;
      }
    }

    builder.AppendLine($"{indent}[card]");
  }

  private static string RenderExtensionAttrsPlain(JsonElement element)
  {
    if (!element.TryGetProperty("attrs", out JsonElement attrs))
    {
      return "[extension]";
    }

    if (attrs.TryGetProperty("text", out JsonElement textEl))
    {
      string text = textEl.GetString() ?? string.Empty;
      if (!string.IsNullOrWhiteSpace(text))
      {
        return text;
      }
    }

    return attrs.TryGetProperty("extensionKey", out JsonElement keyEl)
      ? $"[extension:{keyEl.GetString()}]"
      : "[extension]";
  }

  private static string FormatAdfDateAttr(JsonElement element)
  {
    if (!element.TryGetProperty("attrs", out JsonElement attrs) ||
        !attrs.TryGetProperty("timestamp", out JsonElement ts))
    {
      return "[date]";
    }

    return FormatAdfDateTimestamp(ts.GetString());
  }

  private static string FormatAdfDateTimestamp(string? raw)
  {
    if (string.IsNullOrWhiteSpace(raw))
    {
      return "[date]";
    }

    if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixMs))
    {
      try
      {
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      }
      catch
      {
        return raw;
      }
    }

    if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime dt))
    {
      return dt.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    return raw;
  }

  private static IEnumerable<string> SplitLines(string value) =>
    value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

  [GeneratedRegex(@"^\-\s+\[\s*(?<done>x|X)?\s*\]\s*(?<body>.*)$")]
  private static partial Regex TaskListItemLineRegex();

  [GeneratedRegex(@"^(\d+)\.\s+(.*)$")]
  private static partial Regex OrderedListLineRegex();

  [GeneratedRegex(@"^(?:-{3,}|\*{3,}|_{3,})$")]
  private static partial Regex HorizontalRuleRegex();

  [GeneratedRegex(@"^\|?[\s:\-]+(\|[\s:\-]+)+\|?$")]
  private static partial Regex TableSeparatorRegex();
}
