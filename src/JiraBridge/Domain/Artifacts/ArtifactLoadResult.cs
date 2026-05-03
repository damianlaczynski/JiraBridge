using JiraBridge.Domain.Configuration;

namespace JiraBridge.Domain.Artifacts;

public sealed record ArtifactLoadResult(
  string RepoRoot,
  RepositorySettings RepositorySettings,
  string BacklogRoot,
  RepositoryJiraConfiguration? JiraConfiguration,
  IReadOnlyDictionary<string, ArtifactDocument> Documents,
  List<ValidationIssue> ValidationIssues);
