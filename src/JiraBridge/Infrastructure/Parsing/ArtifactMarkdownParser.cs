using JiraBridge.Domain.Artifacts;

namespace JiraBridge.Infrastructure.Parsing;

public static class ArtifactMarkdownParser
{
  private const string MarkdownLinkPrefix = "[";

  public static ArtifactDocument? TryParse(string path, out List<string> errors)
  {
    errors = [];
    string[] lines = File.ReadAllLines(path);

    string? title = null;
    var sections = new Dictionary<string, SectionContent>(StringComparer.OrdinalIgnoreCase);
    SectionContent? currentSection = null;
    string? currentSectionName = null;
    string? currentNestedSection = null;

    foreach (string rawLine in lines)
    {
      string line = rawLine.TrimEnd();
      string trimmed = line.Trim();

      if (trimmed.StartsWith("# ", StringComparison.Ordinal))
      {
        if (currentSection is not null)
        {
          currentSection.BodyLines.Add(line);
          continue;
        }

        if (title is not null)
        {
          errors.Add("Only one level-1 title is allowed.");
        }

        title = trimmed[2..].Trim();
        currentSection = null;
        currentSectionName = null;
        currentNestedSection = null;
        continue;
      }

      if (trimmed.StartsWith("## ", StringComparison.Ordinal))
      {
        string name = trimmed[3..].Trim();

        if (string.Equals(currentSectionName, "Description", StringComparison.OrdinalIgnoreCase) &&
            !ToolSectionNames.IsToolSection(name))
        {
          if (currentSection is null)
          {
            errors.Add("Description content appeared before the Description section was initialized.");
            continue;
          }

          currentSection.BodyLines.Add(line);
          continue;
        }

        currentSection = new SectionContent();
        sections[name] = currentSection;
        currentSectionName = name;
        currentNestedSection = null;
        continue;
      }

      if (trimmed.StartsWith("### ", StringComparison.Ordinal))
      {
        if (string.Equals(currentSectionName, "Description", StringComparison.OrdinalIgnoreCase))
        {
          currentSection!.BodyLines.Add(line);
          continue;
        }

        if (currentSection is null)
        {
          errors.Add($"Nested section '{trimmed[4..].Trim()}' appears before a parent section.");
          continue;
        }

        currentNestedSection = trimmed[4..].Trim();
        currentSection.NestedLists[currentNestedSection] = [];
        continue;
      }

      if (currentSection is null || string.IsNullOrWhiteSpace(trimmed))
      {
        continue;
      }

      if (trimmed.StartsWith("- ", StringComparison.Ordinal))
      {
        string item = NormalizeMarkdownValue(trimmed[2..].Trim());

        if (currentNestedSection is not null)
        {
          currentSection.NestedLists[currentNestedSection].Add(item);
          continue;
        }

        int separatorIndex = item.IndexOf(':');
        if (separatorIndex > 0)
        {
          string key = item[..separatorIndex].Trim();
          string value = NormalizeMarkdownValue(item[(separatorIndex + 1)..].Trim());
          currentSection.KeyValues[key] = value;
          continue;
        }
      }

      currentSection.BodyLines.Add(line);
    }

    if (string.IsNullOrWhiteSpace(title))
    {
      errors.Add("Missing level-1 title.");
    }

    if (errors.Count > 0 && string.IsNullOrWhiteSpace(title))
    {
      return null;
    }

    return new ArtifactDocument
    {
      Path = path,
      Title = title ?? string.Empty,
      Sections = sections
    };
  }

  private static string NormalizeMarkdownValue(string value)
  {
    if (!value.StartsWith(MarkdownLinkPrefix, StringComparison.Ordinal))
    {
      return value;
    }

    int labelEnd = value.IndexOf(']');
    int openParen = value.IndexOf('(', labelEnd + 1);
    int closeParen = value.LastIndexOf(')');

    if (labelEnd <= 0 || openParen <= labelEnd || closeParen <= openParen)
    {
      return value;
    }

    string linkTarget = value[(openParen + 1)..closeParen].Trim();
    return string.IsNullOrWhiteSpace(linkTarget) ? value : linkTarget;
  }
}
