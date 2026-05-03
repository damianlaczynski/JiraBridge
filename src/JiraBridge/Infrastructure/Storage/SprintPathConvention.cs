using System.Text.RegularExpressions;
using JiraBridge.Domain.Configuration;

namespace JiraBridge.Infrastructure.Storage;

public static partial class SprintPathConvention
{
  private const string SprintsDirectoryName = "sprints";
  private const string SprintDirectoryPrefix = "sprint-";

  public static string BuildRootDirectory(string backlogRoot, string issueType, JiraSprintInfo? sprint)
  {
    string issueTypeDirectory = Slugify(issueType);
    if (sprint is null)
    {
      return Path.Combine(backlogRoot, issueTypeDirectory);
    }

    return Path.Combine(backlogRoot, SprintsDirectoryName, ToSprintDirectoryName(sprint.Name), issueTypeDirectory);
  }

  public static JiraSprintInfo? ResolveSprintForArtifact(
    string artifactPath,
    string backlogRoot,
    IEnumerable<JiraSprintInfo>? sprints)
  {
    if (sprints is null)
    {
      return null;
    }

    string? sprintDirectoryName = TryGetSprintDirectoryName(artifactPath, backlogRoot);
    if (string.IsNullOrWhiteSpace(sprintDirectoryName))
    {
      return null;
    }

    return sprints.FirstOrDefault(sprint =>
      string.Equals(ToSprintDirectoryName(sprint.Name), sprintDirectoryName, StringComparison.OrdinalIgnoreCase));
  }

  public static string? TryExtractSprintDirectoryNameFromPath(string artifactPath)
  {
    string[] segments = artifactPath.Split(
      [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
      StringSplitOptions.RemoveEmptyEntries);

    for (int index = 0; index < segments.Length - 1; index++)
    {
      if (string.Equals(segments[index], SprintsDirectoryName, StringComparison.OrdinalIgnoreCase) &&
          segments[index + 1].StartsWith(SprintDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
      {
        return segments[index + 1];
      }
    }

    return null;
  }

  public static string ToSprintDirectoryName(string sprintName) =>
    $"{SprintDirectoryPrefix}{Slugify(sprintName)}";

  private static string? TryGetSprintDirectoryName(string artifactPath, string backlogRoot)
  {
    string relativePath = Path.GetRelativePath(backlogRoot, artifactPath);
    return TryExtractSprintDirectoryNameFromPath(relativePath);
  }

  private static string Slugify(string value)
  {
    string normalized = value.Trim().ToLowerInvariant();
    normalized = WhitespaceRegex().Replace(normalized, "-");
    normalized = InvalidFileCharsRegex().Replace(normalized, string.Empty);
    normalized = MultiDashRegex().Replace(normalized, "-").Trim('-');
    return string.IsNullOrWhiteSpace(normalized) ? "item" : normalized;
  }

  [GeneratedRegex(@"\s+")]
  private static partial Regex WhitespaceRegex();

  [GeneratedRegex(@"[^a-z0-9\-]")]
  private static partial Regex InvalidFileCharsRegex();

  [GeneratedRegex(@"\-+")]
  private static partial Regex MultiDashRegex();
}
