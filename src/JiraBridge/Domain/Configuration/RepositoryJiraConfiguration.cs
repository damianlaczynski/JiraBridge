namespace JiraBridge.Domain.Configuration;

public sealed record RepositoryJiraConfiguration(
  string ProjectKey,
  string ProjectId,
  string ProjectName,
  string BaseUrl,
  List<JiraProjectIssueType> IssueTypes,
  List<JiraLinkType> LinkTypes,
  List<JiraIssueTypeStatuses> IssueTypeStatuses,
  string? SprintFieldId = null,
  List<JiraSprintInfo>? Sprints = null);

public sealed record JiraProjectIssueType(
  string Id,
  string Name,
  bool Subtask);

public sealed record JiraLinkType(
  string Id,
  string Name,
  string Inward,
  string Outward);

public sealed record JiraIssueTypeStatuses(
  string IssueTypeId,
  string IssueTypeName,
  List<JiraStatus> Statuses);

public sealed record JiraStatus(
  string Id,
  string Name,
  string Category);

public sealed record JiraSprintInfo(
  int Id,
  string Name,
  string State,
  int BoardId);
