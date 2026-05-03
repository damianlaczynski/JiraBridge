namespace JiraBridge.Domain.Artifacts;

public enum PlanAction
{
  Create = 0,
  Update = 1
}

public sealed record PlanItem(
  PlanAction Action,
  string Type,
  string Title,
  string RelativePath,
  string JiraProject,
  string? JiraIssueKey,
  string? ParentReference,
  IReadOnlyList<string> DependsOn);
