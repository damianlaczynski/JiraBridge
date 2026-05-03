using System.Linq;
using JiraBridge.Domain.Artifacts;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Repository;
using JiraBridge.Infrastructure.Storage;

namespace JiraBridge.Infrastructure.Jira;

public static class JiraPayloadMapper
{
  public static JiraIssuePayload Map(
    ArtifactDocument document,
    IReadOnlyDictionary<string, ArtifactDocument> documents,
    IReadOnlyDictionary<string, string> runtimeIssueKeys,
    RepositoryJiraConfiguration jiraConfiguration,
    string repoRoot) =>
    Map(
      document,
      documents,
      runtimeIssueKeys,
      jiraConfiguration,
      repoRoot,
      Path.Combine(repoRoot, RepositoryLayout.Default.BacklogRoot),
      sprintMappingEnabled: false);

  public static JiraIssuePayload Map(
    ArtifactDocument document,
    IReadOnlyDictionary<string, ArtifactDocument> documents,
    IReadOnlyDictionary<string, string> runtimeIssueKeys,
    RepositoryJiraConfiguration jiraConfiguration,
    string repoRoot,
    string backlogRoot,
    bool sprintMappingEnabled)
  {
    string? parentIssueKey = null;
    string? parentArtifactPath = null;
    ResolvePublishParent(document, documents, runtimeIssueKeys, repoRoot, out parentIssueKey, out parentArtifactPath);
    int? sprintId = ResolveSprintId(document, jiraConfiguration, backlogRoot, sprintMappingEnabled);

    var relationships = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
    if (document.Sections.TryGetValue("Relations", out SectionContent? relationsSection))
    {
      foreach ((string relationshipName, List<string> values) in relationsSection.NestedLists)
      {
        List<string> items = values
          .Where(item => !PathResolver.IsNone(item))
          .Select(item => ToRelationshipReference(document, documents, runtimeIssueKeys, repoRoot, item))
          .ToList();

        relationships[relationshipName] = items;
      }
    }

    return new JiraIssuePayload(
      ProjectKey: jiraConfiguration.ProjectKey,
      IssueType: document.JiraIssueType ?? string.Empty,
      Summary: document.Title,
      Description: document.GetSectionBody("Description"),
      ApplySprintMapping: sprintMappingEnabled,
      SprintId: sprintId,
      ExistingIssueKey: Normalize(document.JiraIssueKey),
      ParentIssueKey: parentIssueKey,
      ParentArtifactPath: parentArtifactPath,
      Relationships: relationships);
  }

  private static int? ResolveSprintId(
    ArtifactDocument document,
    RepositoryJiraConfiguration jiraConfiguration,
    string backlogRoot,
    bool sprintMappingEnabled)
  {
    if (!sprintMappingEnabled)
    {
      return null;
    }

    JiraSprintInfo? sprint = SprintPathConvention.ResolveSprintForArtifact(document.Path, backlogRoot, jiraConfiguration.Sprints);
    string? sprintDirectoryName = SprintPathConvention.TryExtractSprintDirectorySegment(document.Path, backlogRoot);
    if (sprint is not null)
    {
      return sprint.Id;
    }

    if (string.IsNullOrWhiteSpace(sprintDirectoryName))
    {
      return null;
    }

    IReadOnlyList<JiraSprintInfo>? sprints = jiraConfiguration.Sprints;
    if (sprints is null || sprints.Count == 0)
    {
      return null;
    }

    IEnumerable<string> available = sprints.Select(info => SprintPathConvention.ToSprintDirectoryName(info.Name)).Distinct();
    throw new InvalidOperationException(
      $"Artifact '{document.Path}' points to sprint directory '{sprintDirectoryName}', but no Jira sprint matched that folder name. " +
      $"Known sprint folder segments from Jira: {string.Join(", ", available.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))}.");
  }

  private static void ResolvePublishParent(
    ArtifactDocument document,
    IReadOnlyDictionary<string, ArtifactDocument> documents,
    IReadOnlyDictionary<string, string> runtimeIssueKeys,
    string repoRoot,
    out string? parentIssueKey,
    out string? parentArtifactPath)
  {
    parentIssueKey = null;
    parentArtifactPath = null;

    if (string.IsNullOrWhiteSpace(document.Parent) || PathResolver.IsNone(document.Parent))
    {
      return;
    }

    string directParentPath = PathResolver.ResolveArtifactRelativePath(document.Path, document.Parent);

    parentArtifactPath = Path.GetRelativePath(repoRoot, directParentPath);
    parentIssueKey = ResolveIssueKey(directParentPath, documents, runtimeIssueKeys);
  }

  private static string? ResolveIssueKey(
    string artifactPath,
    IReadOnlyDictionary<string, ArtifactDocument> documents,
    IReadOnlyDictionary<string, string> runtimeIssueKeys)
  {
    if (runtimeIssueKeys.TryGetValue(artifactPath, out string? runtimeIssueKey))
    {
      return runtimeIssueKey;
    }

    if (documents.TryGetValue(artifactPath, out ArtifactDocument? relatedArtifact) &&
        !string.IsNullOrWhiteSpace(relatedArtifact.JiraIssueKey))
    {
      return relatedArtifact.JiraIssueKey;
    }

    return null;
  }

  private static string ToRelationshipReference(
    ArtifactDocument document,
    IReadOnlyDictionary<string, ArtifactDocument> documents,
    IReadOnlyDictionary<string, string> runtimeIssueKeys,
    string repoRoot,
    string item)
  {
    string resolvedPath = PathResolver.ResolveArtifactRelativePath(document.Path, item);

    if (runtimeIssueKeys.TryGetValue(resolvedPath, out string? runtimeIssueKey))
    {
      return runtimeIssueKey;
    }

    if (documents.TryGetValue(resolvedPath, out ArtifactDocument? relatedArtifact))
    {
      if (!string.IsNullOrWhiteSpace(relatedArtifact.JiraIssueKey))
      {
        return relatedArtifact.JiraIssueKey!;
      }

      return Path.GetRelativePath(repoRoot, resolvedPath);
    }

    return Path.GetRelativePath(repoRoot, resolvedPath);
  }

  private static string? Normalize(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;
}
