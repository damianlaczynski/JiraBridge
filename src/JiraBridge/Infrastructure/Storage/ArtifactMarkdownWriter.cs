using System.Text;
using JiraBridge.Domain.Artifacts;
using JiraBridge.Infrastructure.Repository;

namespace JiraBridge.Infrastructure.Storage;

public static class ArtifactMarkdownWriter
{
  public static void Write(string filePath, ArtifactDocument document)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

    var builder = new StringBuilder();
    builder.AppendLine($"# {document.Title}");
    builder.AppendLine();

    WriteDescription(builder, document);
    WriteLinks(builder, document);
    WriteRelations(builder, document);
    WriteMetadata(builder, document);

    File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
  }

  private static void WriteDescription(StringBuilder builder, ArtifactDocument document)
  {
    builder.AppendLine("## Description");
    builder.AppendLine();

    if (document.Sections.TryGetValue("Description", out SectionContent? description) &&
        description.BodyLines.Count > 0)
    {
      foreach (string line in description.BodyLines)
      {
        builder.AppendLine(line);
      }
    }

    builder.AppendLine();
  }

  private static void WriteLinks(StringBuilder builder, ArtifactDocument document)
  {
    builder.AppendLine("## Links");
    builder.AppendLine();

    string parent = document.Parent ?? "none";
    builder.AppendLine($"- Parent: {FormatFileReference(parent)}");
    builder.AppendLine();
  }

  private static void WriteRelations(StringBuilder builder, ArtifactDocument document)
  {
    builder.AppendLine("## Relations");
    builder.AppendLine();

    if (!document.Sections.TryGetValue("Relations", out SectionContent? relations) ||
        relations.NestedLists.Count == 0)
    {
      builder.AppendLine("### Relates");
      builder.AppendLine();
      builder.AppendLine("- none");
      builder.AppendLine();
      return;
    }

    foreach ((string relationName, List<string> values) in relations.NestedLists.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
    {
      builder.AppendLine($"### {relationName}");
      builder.AppendLine();

      if (values.Count == 0)
      {
        builder.AppendLine("- none");
      }
      else
      {
        foreach (string value in values)
        {
          builder.AppendLine($"- {FormatFileReference(value)}");
        }
      }

      builder.AppendLine();
    }
  }

  private static void WriteMetadata(StringBuilder builder, ArtifactDocument document)
  {
    builder.AppendLine("## Metadata");
    builder.AppendLine();

    if (document.Sections.TryGetValue("Metadata", out SectionContent? metadata))
    {
      string[] preferredOrder =
      [
        "Issue Type",
        "Jira Issue Key",
        "Jira Last Synced Local Hash",
        "Jira Last Synced Remote Hash"
      ];

      foreach (string key in preferredOrder.Where(metadata.KeyValues.ContainsKey))
      {
        builder.AppendLine($"- {key}: {metadata.KeyValues[key]}");
      }

      foreach ((string key, string value) in metadata.KeyValues
        .Where(item => !preferredOrder.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
      {
        builder.AppendLine($"- {key}: {value}");
      }
    }

    builder.AppendLine();
  }

  private static string FormatFileReference(string? path)
  {
    if (string.IsNullOrWhiteSpace(path) || PathResolver.IsNone(path))
    {
      return "none";
    }

    return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
      ? $"[{path}]({path})"
      : path;
  }
}
