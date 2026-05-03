using System.Text.RegularExpressions;
using JiraBridge.Domain.Configuration;

namespace JiraBridge.Infrastructure.Storage;

public static partial class SprintPathConvention
{
  public const string BacklogBucketSegment = "backlog";
  private const string LegacySprintsDirectoryName = "sprints";
  private const string SprintDirectoryPrefix = "sprint-";

  public static string BuildPlacementDirectory(string backlogRoot, JiraSprintInfo? sprint, bool sprintMappingEnabled)
  {
    if (!sprintMappingEnabled || sprint is null)
    {
      return Path.Combine(backlogRoot, BacklogBucketSegment);
    }

    return Path.Combine(backlogRoot, ToSprintDirectoryName(sprint.Name));
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

    string? sprintDirectoryName = TryExtractSprintDirectorySegment(artifactPath, backlogRoot);
    if (string.IsNullOrWhiteSpace(sprintDirectoryName))
    {
      return null;
    }

    return sprints.FirstOrDefault(sprint =>
      string.Equals(ToSprintDirectoryName(sprint.Name), sprintDirectoryName, StringComparison.OrdinalIgnoreCase));
  }

  public static string? TryExtractSprintDirectorySegment(string artifactPath, string? backlogRoot)
  {
    if (!string.IsNullOrWhiteSpace(backlogRoot))
    {
      try
      {
        string relative = Path.GetRelativePath(Path.GetFullPath(backlogRoot), Path.GetFullPath(artifactPath));
        if (!relative.StartsWith("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative))
        {
          string? fromRelative = ExtractSprintFromRelativePath(relative);
          if (fromRelative is not null)
          {
            return fromRelative;
          }
        }
      }
      catch (ArgumentException)
      {
      }
    }

    return ExtractSprintFromAnyPath(artifactPath);
  }

  public static string ToSprintDirectoryName(string sprintName) =>
    $"{SprintDirectoryPrefix}{Slugify(sprintName)}";

  private static string? ExtractSprintFromRelativePath(string relativePath)
  {
    string[] segments = relativePath.Split(
      [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
      StringSplitOptions.RemoveEmptyEntries);

    if (segments.Length == 0)
    {
      return null;
    }

    if (string.Equals(segments[0], BacklogBucketSegment, StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    if (segments[0].StartsWith(SprintDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
    {
      return segments[0];
    }

    for (int index = 0; index < segments.Length - 1; index++)
    {
      if (string.Equals(segments[index], LegacySprintsDirectoryName, StringComparison.OrdinalIgnoreCase) &&
          segments[index + 1].StartsWith(SprintDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
      {
        return segments[index + 1];
      }
    }

    return null;
  }

  private static string? ExtractSprintFromAnyPath(string artifactPath)
  {
    string[] segments = artifactPath.Split(
      [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
      StringSplitOptions.RemoveEmptyEntries);

    for (int index = 0; index < segments.Length - 1; index++)
    {
      if (string.Equals(segments[index], LegacySprintsDirectoryName, StringComparison.OrdinalIgnoreCase) &&
          segments[index + 1].StartsWith(SprintDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
      {
        return segments[index + 1];
      }
    }

    foreach (string segment in segments)
    {
      if (segment.StartsWith(SprintDirectoryPrefix, StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(segment, BacklogBucketSegment, StringComparison.OrdinalIgnoreCase))
      {
        return segment;
      }
    }

    return null;
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
