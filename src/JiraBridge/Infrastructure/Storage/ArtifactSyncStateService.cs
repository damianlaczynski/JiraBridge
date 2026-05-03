using System.Security.Cryptography;
using System.Text;
using JiraBridge.Domain.Artifacts;
using JiraBridge.Infrastructure.Jira;

namespace JiraBridge.Infrastructure.Storage;

public static class ArtifactSyncStateService
{
  public static string ComputeLocalFingerprint(ArtifactDocument document, string? backlogRoot = null)
  {
    var builder = new StringBuilder();
    builder.AppendLine(document.Title.Trim());
    builder.AppendLine(document.JiraIssueType?.Trim() ?? string.Empty);
    builder.AppendLine(SprintPathConvention.TryExtractSprintDirectorySegment(document.Path, backlogRoot) ?? string.Empty);
    builder.AppendLine(document.Parent?.Trim() ?? string.Empty);
    builder.AppendLine(document.GetSectionBody("Description"));

    if (document.Sections.TryGetValue("Relations", out SectionContent? relations))
    {
      foreach ((string relationName, List<string> items) in relations.NestedLists.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
      {
        builder.AppendLine(relationName);
        foreach (string item in items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
          builder.AppendLine(item.Trim());
        }
      }
    }

    return ComputeHash(builder.ToString());
  }

  public static string ComputeRemoteFingerprint(JiraRemoteIssue remoteIssue)
  {
    var builder = new StringBuilder();
    builder.AppendLine(remoteIssue.Summary.Trim());
    builder.AppendLine(remoteIssue.IssueType.Trim());
    builder.AppendLine(remoteIssue.Sprint?.Name.Trim() ?? string.Empty);
    builder.AppendLine(remoteIssue.ParentIssueKey?.Trim() ?? string.Empty);
    builder.AppendLine(remoteIssue.Description.Trim());

    foreach (IGrouping<string, JiraRemoteLink> relationGroup in remoteIssue.Links
      .Where(link => !string.IsNullOrWhiteSpace(link.LinkType))
      .GroupBy(link => link.LinkType, StringComparer.OrdinalIgnoreCase)
      .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
    {
      builder.AppendLine(relationGroup.Key);
      foreach (string issueKey in relationGroup
        .Select(link => link.OutwardIssueKey ?? link.InwardIssueKey)
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .Select(key => key!)
        .OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
      {
        builder.AppendLine(issueKey);
      }
    }

    return ComputeHash(builder.ToString());
  }

  public static bool HasLocalChanges(ArtifactDocument document)
  {
    string? lastSyncedLocalHash = document.JiraLastSyncedLocalHash;
    if (string.IsNullOrWhiteSpace(document.JiraIssueKey))
    {
      return true;
    }

    if (string.IsNullOrWhiteSpace(lastSyncedLocalHash))
    {
      return true;
    }

    return !string.Equals(ComputeLocalFingerprint(document, null), lastSyncedLocalHash, StringComparison.OrdinalIgnoreCase);
  }

  public static bool HasRemoteChanges(ArtifactDocument document, JiraRemoteIssue remoteIssue)
  {
    string? lastSyncedRemoteHash = document.JiraLastSyncedRemoteHash;
    if (string.IsNullOrWhiteSpace(document.JiraIssueKey))
    {
      return false;
    }

    if (string.IsNullOrWhiteSpace(lastSyncedRemoteHash))
    {
      return true;
    }

    return !string.Equals(ComputeRemoteFingerprint(remoteIssue), lastSyncedRemoteHash, StringComparison.OrdinalIgnoreCase);
  }

  private static string ComputeHash(string value)
  {
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(hash);
  }
}
