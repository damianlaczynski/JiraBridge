using System.Text;
using System.Text.RegularExpressions;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.Infrastructure.Repository;

namespace JiraBridge.Infrastructure.Storage;

public static partial class ArtifactImportWriter
{
  public static string BuildPlannedArtifactPath(string backlogRoot, JiraRemoteIssue issue, string? parentPath)
  {
    string fileName = $"{issue.IssueKey.ToLowerInvariant()}-{Slugify(issue.Summary)}.md";
    string directory = string.IsNullOrWhiteSpace(parentPath)
      ? SprintPathConvention.BuildRootDirectory(backlogRoot, issue.IssueType, issue.Sprint)
      : BuildChildDirectoryPath(parentPath);
    return Path.Combine(directory, fileName);
  }

  public static void WriteImportedArtifact(
    string filePath,
    JiraRemoteIssue issue,
    string? parentRelativePath,
    IReadOnlyDictionary<string, IReadOnlyList<string>> relations)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

    var builder = new StringBuilder();
    builder.AppendLine($"# {issue.Summary}");
    builder.AppendLine();
    builder.AppendLine("## Description");
    builder.AppendLine();
    builder.AppendLine(string.IsNullOrWhiteSpace(SanitizeImportedDescription(issue.Description))
      ? "Imported from Jira."
      : SanitizeImportedDescription(issue.Description));
    builder.AppendLine();
    builder.AppendLine("## Links");
    builder.AppendLine();
    builder.AppendLine($"- Parent: {FormatFileReference(parentRelativePath)}");
    builder.AppendLine();
    builder.AppendLine("## Relations");
    builder.AppendLine();

    if (relations.Count == 0)
    {
      builder.AppendLine("### Relates");
      builder.AppendLine();
      builder.AppendLine("- none");
      builder.AppendLine();
    }
    else
    {
      foreach ((string relationName, IReadOnlyList<string> values) in relations.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
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

    builder.AppendLine("## Metadata");
    builder.AppendLine();
    builder.AppendLine($"- Issue Type: {issue.IssueType}");
    builder.AppendLine($"- Jira Issue Key: {issue.IssueKey}");
    builder.AppendLine("- Jira Last Synced Local Hash:");
    builder.AppendLine("- Jira Last Synced Remote Hash:");
    builder.AppendLine();

    File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
  }

  private static string FormatFileReference(string? path)
  {
    if (string.IsNullOrWhiteSpace(path) || PathResolver.IsNone(path))
    {
      return "none";
    }

    return $"[{path}]({path})";
  }

  private static string BuildChildDirectoryPath(string parentPath)
  {
    string parentDirectory = Path.GetDirectoryName(parentPath)
      ?? throw new InvalidOperationException($"Could not determine parent directory for '{parentPath}'.");
    string parentFileName = Path.GetFileNameWithoutExtension(parentPath);
    return Path.Combine(parentDirectory, parentFileName);
  }

  private static string Slugify(string value)
  {
    string normalized = value.Trim().ToLowerInvariant();
    normalized = WhitespaceRegex().Replace(normalized, "-");
    normalized = InvalidFileCharsRegex().Replace(normalized, string.Empty);
    normalized = MultiDashRegex().Replace(normalized, "-").Trim('-');

    return string.IsNullOrWhiteSpace(normalized) ? "item" : normalized;
  }

  private static string SanitizeImportedDescription(string description)
  {
    string trimmed = description.Trim();
    const string remoteSectionHeader = "Remote Jira description:";

    int mergeNotesIndex = trimmed.IndexOf("Merge notes:", StringComparison.Ordinal);
    int remoteSectionIndex = trimmed.IndexOf(remoteSectionHeader, StringComparison.Ordinal);

    if (mergeNotesIndex >= 0 && remoteSectionIndex > mergeNotesIndex)
    {
      string remotePart = trimmed[(remoteSectionIndex + remoteSectionHeader.Length)..].Trim();
      if (!string.IsNullOrWhiteSpace(remotePart))
      {
        return remotePart;
      }
    }

    return trimmed;
  }

  [GeneratedRegex(@"\s+")]
  private static partial Regex WhitespaceRegex();

  [GeneratedRegex(@"[^a-z0-9\-]")]
  private static partial Regex InvalidFileCharsRegex();

  [GeneratedRegex(@"\-+")]
  private static partial Regex MultiDashRegex();
}
