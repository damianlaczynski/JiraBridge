using JiraBridge.Domain.Artifacts;
using JiraBridge.Infrastructure.Repository;

namespace JiraBridge.Infrastructure.Storage;

public static class PlanBuilder
{
  public static List<ArtifactDocument> OrderDocuments(List<ArtifactDocument> documents)
  {
    var documentsByPath = documents.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
    var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

    foreach (ArtifactDocument document in documents)
    {
      var requiredBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      if (!string.IsNullOrWhiteSpace(document.Parent) && !PathResolver.IsNone(document.Parent))
      {
        string parentPath = PathResolver.ResolveArtifactRelativePath(document.Path, document.Parent);
        if (documentsByPath.ContainsKey(parentPath))
        {
          requiredBefore.Add(parentPath);
        }
      }

      if (document.Sections.TryGetValue("Relations", out SectionContent? relations) &&
          relations.NestedLists.TryGetValue("Blocks", out List<string>? dependsOn))
      {
        foreach (string dependency in dependsOn.Where(x => !PathResolver.IsNone(x)))
        {
          string dependencyPath = PathResolver.ResolveArtifactRelativePath(document.Path, dependency);
          if (documentsByPath.ContainsKey(dependencyPath))
          {
            requiredBefore.Add(dependencyPath);
          }
        }
      }

      dependencies[document.Path] = requiredBefore;
    }

    var ordered = new List<ArtifactDocument>();
    var remaining = new HashSet<string>(documentsByPath.Keys, StringComparer.OrdinalIgnoreCase);

    while (remaining.Count > 0)
    {
      List<string> ready = remaining
        .Where(path => dependencies[path].All(dependency => !remaining.Contains(dependency)))
        .OrderBy(path => ParentSortOrder(documentsByPath[path]))
        .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();

      if (ready.Count == 0)
      {
        ready = remaining
          .OrderBy(path => ParentSortOrder(documentsByPath[path]))
          .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
          .ToList();
      }

      foreach (string path in ready)
      {
        ordered.Add(documentsByPath[path]);
        remaining.Remove(path);
      }
    }

    return ordered;
  }

  public static PlanItem CreatePlanItem(ArtifactDocument document, string repoRoot, string jiraProjectKey)
  {
    string? parentReference = null;
    if (!string.IsNullOrWhiteSpace(document.Parent) && !PathResolver.IsNone(document.Parent))
    {
      parentReference = document.Parent!;
    }

    List<string> dependsOn = [];
    if (document.Sections.TryGetValue("Relations", out SectionContent? relations) &&
        relations.NestedLists.TryGetValue("Blocks", out List<string>? dependencyItems))
    {
      dependsOn = dependencyItems.Where(item => !PathResolver.IsNone(item)).ToList();
    }

    return new PlanItem(
      string.IsNullOrWhiteSpace(document.JiraIssueKey) ? PlanAction.Create : PlanAction.Update,
      document.JiraIssueType ?? "unknown",
      document.Title,
      document.RelativePath(repoRoot),
      jiraProjectKey,
      string.IsNullOrWhiteSpace(document.JiraIssueKey) ? null : document.JiraIssueKey,
      parentReference,
      dependsOn);
  }

  private static int ParentSortOrder(ArtifactDocument document) =>
    string.IsNullOrWhiteSpace(document.Parent) || PathResolver.IsNone(document.Parent) ? 0 : 1;
}
