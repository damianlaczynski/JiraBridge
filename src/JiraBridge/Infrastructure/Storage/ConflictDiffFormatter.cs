using System.Text;
using JiraBridge.Domain.Artifacts;
using JiraBridge.Infrastructure.Jira;

namespace JiraBridge.Infrastructure.Storage;

public static class ConflictDiffFormatter
{
  public static string Build(ArtifactDocument document, JiraIssuePayload localPayload, JiraRemoteIssue remoteIssue)
  {
    var lines = new List<string>();

    AddField(lines, "Summary", localPayload.Summary, remoteIssue.Summary);
    AddField(lines, "Issue Type", localPayload.IssueType, remoteIssue.IssueType);
    AddField(lines, "Parent", localPayload.ParentIssueKey ?? "none", remoteIssue.ParentIssueKey ?? "none");

    string relationDiff = BuildRelationDiff(localPayload.Relationships, remoteIssue.Links);
    if (!string.IsNullOrWhiteSpace(relationDiff))
    {
      lines.Add("Relations:");
      lines.AddRange(relationDiff.Split(System.Environment.NewLine));
    }

    string descriptionDiff = BuildLineDiff(
      localPayload.Description,
      remoteIssue.Description,
      "Repository Description",
      "Jira Description");

    if (!string.IsNullOrWhiteSpace(descriptionDiff))
    {
      if (lines.Count > 0)
      {
        lines.Add(string.Empty);
      }

      lines.Add(descriptionDiff);
    }

    return string.Join(System.Environment.NewLine, lines).Trim();
  }

  private static void AddField(List<string> lines, string fieldName, string localValue, string remoteValue)
  {
    if (string.Equals(localValue, remoteValue, StringComparison.Ordinal))
    {
      return;
    }

    lines.Add($"{fieldName}:");
    lines.Add($"  repo : {Display(localValue)}");
    lines.Add($"  jira : {Display(remoteValue)}");
  }

  private static string BuildRelationDiff(
    IReadOnlyDictionary<string, IReadOnlyList<string>> localRelationships,
    IReadOnlyList<JiraRemoteLink> remoteLinks)
  {
    var remoteByType = remoteLinks
      .Where(link => !string.IsNullOrWhiteSpace(link.LinkType))
      .GroupBy(link => link.LinkType, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        group => group.Key,
        group => group
          .Select(link => link.OutwardIssueKey ?? link.InwardIssueKey)
          .Where(value => !string.IsNullOrWhiteSpace(value))
          .Select(value => value!)
          .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
          .ToList() as IReadOnlyList<string>,
        StringComparer.OrdinalIgnoreCase);

    List<string> allKeys = localRelationships.Keys
      .Concat(remoteByType.Keys)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var lines = new List<string>();
    foreach (string key in allKeys)
    {
      IReadOnlyList<string> localValues = localRelationships.TryGetValue(key, out IReadOnlyList<string>? local)
        ? local
        : Array.Empty<string>();
      IReadOnlyList<string> remoteValues = remoteByType.TryGetValue(key, out IReadOnlyList<string>? remote)
        ? remote
        : Array.Empty<string>();

      string localJoined = string.Join(", ", localValues);
      string remoteJoined = string.Join(", ", remoteValues);
      if (string.Equals(localJoined, remoteJoined, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      lines.Add($"  {key}:");
      lines.Add($"    repo : {Display(localJoined)}");
      lines.Add($"    jira : {Display(remoteJoined)}");
    }

    return string.Join(System.Environment.NewLine, lines);
  }

  private static string BuildLineDiff(string localText, string remoteText, string localLabel, string remoteLabel)
  {
    string normalizedLocal = Normalize(localText);
    string normalizedRemote = Normalize(remoteText);
    if (string.Equals(normalizedLocal, normalizedRemote, StringComparison.Ordinal))
    {
      return string.Empty;
    }

    string[] localLines = normalizedLocal.Split('\n');
    string[] remoteLines = normalizedRemote.Split('\n');
    List<string> diffLines = BuildDiffLines(localLines, remoteLines);

    var builder = new StringBuilder();
    builder.AppendLine($"{localLabel} vs {remoteLabel}:");
    foreach (string line in diffLines)
    {
      builder.AppendLine(line);
    }

    return builder.ToString().TrimEnd();
  }

  private static List<string> BuildDiffLines(string[] left, string[] right)
  {
    int[,] lcs = new int[left.Length + 1, right.Length + 1];
    for (int i = left.Length - 1; i >= 0; i--)
    {
      for (int j = right.Length - 1; j >= 0; j--)
      {
        lcs[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
          ? lcs[i + 1, j + 1] + 1
          : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
      }
    }

    int leftIndex = 0;
    int rightIndex = 0;
    var result = new List<string>();
    while (leftIndex < left.Length && rightIndex < right.Length)
    {
      if (string.Equals(left[leftIndex], right[rightIndex], StringComparison.Ordinal))
      {
        result.Add($"  {left[leftIndex]}");
        leftIndex++;
        rightIndex++;
      }
      else if (lcs[leftIndex + 1, rightIndex] >= lcs[leftIndex, rightIndex + 1])
      {
        result.Add($"- {left[leftIndex]}");
        leftIndex++;
      }
      else
      {
        result.Add($"+ {right[rightIndex]}");
        rightIndex++;
      }
    }

    while (leftIndex < left.Length)
    {
      result.Add($"- {left[leftIndex]}");
      leftIndex++;
    }

    while (rightIndex < right.Length)
    {
      result.Add($"+ {right[rightIndex]}");
      rightIndex++;
    }

    return result;
  }

  private static string Normalize(string value) =>
    value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

  private static string Display(string value) =>
    string.IsNullOrWhiteSpace(value) ? "(empty)" : value;
}
