using JiraBridge.Domain.Configuration;

namespace JiraBridge.Infrastructure.Jira;

public sealed record JiraRemoteIssue(
  string IssueKey,
  string IssueType,
  string Status,
  string Summary,
  string Description,
  DateTimeOffset UpdatedAt,
  JiraSprintInfo? Sprint,
  string? ParentIssueKey,
  IReadOnlyList<JiraRemoteLink> Links)
{
  public JiraRemoteIssue(
    string issueKey,
    string issueType,
    string status,
    string summary,
    string description,
    DateTimeOffset updatedAt,
    string? parentIssueKey,
    IReadOnlyList<JiraRemoteLink> links)
    : this(issueKey, issueType, status, summary, description, updatedAt, null, parentIssueKey, links)
  {
  }
}

public sealed record JiraRemoteLink(
  string LinkType,
  string? InwardIssueKey,
  string? OutwardIssueKey);
