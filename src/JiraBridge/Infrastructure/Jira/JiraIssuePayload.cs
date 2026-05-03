namespace JiraBridge.Infrastructure.Jira;

public sealed record JiraIssuePayload(
  string ProjectKey,
  string IssueType,
  string Summary,
  string Description,
  bool ApplySprintMapping,
  int? SprintId,
  string? ExistingIssueKey,
  string? ParentIssueKey,
  string? ParentArtifactPath,
  IReadOnlyDictionary<string, IReadOnlyList<string>> Relationships)
{
  public JiraIssuePayload(
    string projectKey,
    string issueType,
    string summary,
    string description,
    string? existingIssueKey,
    string? parentIssueKey,
    string? parentArtifactPath,
    IReadOnlyDictionary<string, IReadOnlyList<string>> relationships)
    : this(projectKey, issueType, summary, description, false, null, existingIssueKey, parentIssueKey, parentArtifactPath, relationships)
  {
  }
}
