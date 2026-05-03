using JiraBridge.Domain.Artifacts;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Parsing;

namespace JiraBridge.Infrastructure.Repository;

public static class ArtifactRepository
{
  public static ArtifactLoadResult? LoadArtifacts(
    string repoRoot,
    RepositorySettings repositorySettings,
    bool writeErrors,
    bool allowEmptyBacklog = false)
  {
    string backlogRoot = PathResolver.ResolveRepoRelativePath(repoRoot, repositorySettings.BacklogRoot);

    if (!Directory.Exists(backlogRoot))
    {
      if (writeErrors)
      {
        Console.Error.WriteLine($"Backlog directory not found: {backlogRoot}");
      }

      return null;
    }

    string[] files = Directory.GetFiles(backlogRoot, "*.md", SearchOption.AllDirectories);
    if (files.Length == 0)
    {
      if (allowEmptyBacklog)
      {
        files = [];
      }
      else
      {
        if (writeErrors)
        {
          Console.Error.WriteLine($"No markdown artifacts found under: {backlogRoot}");
        }

        return null;
      }
    }

    var documents = new Dictionary<string, ArtifactDocument>(StringComparer.OrdinalIgnoreCase);
    var parseErrors = new List<ValidationIssue>();

    RepositoryJiraConfiguration? jiraConfiguration =
      RepositoryJiraConfigurationStore.TryLoad(repoRoot, repositorySettings, out string? configurationError);

    if (jiraConfiguration is null && configurationError is not null)
    {
      parseErrors.Add(new ValidationIssue(RepositoryJiraConfigurationStore.GetPath(repoRoot, repositorySettings), configurationError));
    }

    foreach (string file in files)
    {
      ArtifactDocument? document = ArtifactMarkdownParser.TryParse(file, out List<string> errors);
      if (document is null)
      {
        parseErrors.AddRange(errors.Select(error => new ValidationIssue(file, error)));
        continue;
      }

      documents[file] = document;
      parseErrors.AddRange(errors.Select(error => new ValidationIssue(file, error)));
    }

    var issues = new List<ValidationIssue>(parseErrors);
    if (jiraConfiguration is not null)
    {
      issues.AddRange(RepositoryJiraConfigurationValidator.Validate(jiraConfiguration, repoRoot, repositorySettings));
    }

    foreach (ArtifactDocument document in documents.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
    {
      issues.AddRange(ArtifactValidator.Validate(document, documents, jiraConfiguration, repoRoot));
    }

    return new ArtifactLoadResult(repoRoot, repositorySettings, backlogRoot, jiraConfiguration, documents, issues);
  }
}
