using JiraBridge.Domain.Configuration;

namespace JiraBridge.Infrastructure.Parsing;

public static class JiraMetadataResolver
{
  public static JiraProjectIssueType? ResolveIssueType(
    string requestedIssueType,
    IReadOnlyList<JiraProjectIssueType> issueTypes)
  {
    JiraProjectIssueType? exactMatch = issueTypes.FirstOrDefault(type =>
      string.Equals(type.Name, requestedIssueType, StringComparison.OrdinalIgnoreCase));
    if (exactMatch is not null)
    {
      return exactMatch;
    }

    string normalizedRequested = NormalizeName(requestedIssueType);
    return issueTypes.FirstOrDefault(type => NormalizeName(type.Name) == normalizedRequested);
  }

  public static JiraLinkType? ResolveLinkType(string requestedLinkType, IReadOnlyList<JiraLinkType> linkTypes)
  {
    JiraLinkType? exactMatch = linkTypes.FirstOrDefault(type =>
      string.Equals(type.Name, requestedLinkType, StringComparison.OrdinalIgnoreCase));
    if (exactMatch is not null)
    {
      return exactMatch;
    }

    string normalizedRequested = NormalizeName(requestedLinkType);
    return linkTypes.FirstOrDefault(type => NormalizeName(type.Name) == normalizedRequested);
  }

  public static JiraStatus? ResolveStatus(
    string issueTypeName,
    string requestedStatus,
    IReadOnlyList<JiraIssueTypeStatuses> issueTypeStatuses)
  {
    JiraIssueTypeStatuses? issueTypeStatusConfiguration = issueTypeStatuses.FirstOrDefault(item =>
      NormalizeName(item.IssueTypeName) == NormalizeName(issueTypeName));

    if (issueTypeStatusConfiguration is null)
    {
      return null;
    }

    JiraStatus? exactMatch = issueTypeStatusConfiguration.Statuses.FirstOrDefault(status =>
      string.Equals(status.Name, requestedStatus, StringComparison.OrdinalIgnoreCase));
    if (exactMatch is not null)
    {
      return exactMatch;
    }

    string normalizedRequested = NormalizeName(requestedStatus);
    return issueTypeStatusConfiguration.Statuses.FirstOrDefault(status =>
      NormalizeName(status.Name) == normalizedRequested);
  }

  private static string NormalizeName(string value) =>
    value
      .Replace("-", string.Empty, StringComparison.Ordinal)
      .Replace(" ", string.Empty, StringComparison.Ordinal)
      .Trim()
      .ToLowerInvariant();
}
