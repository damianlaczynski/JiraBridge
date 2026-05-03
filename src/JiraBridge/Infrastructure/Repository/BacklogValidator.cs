using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;
using JiraBridge.Domain.Artifacts;
using JiraBridge.Domain.Configuration;

namespace JiraBridge.Infrastructure.Repository;

public sealed class BacklogValidator(IRepositoryMetadataRefresher metadataRefresher) : IBacklogValidator
{
  public async Task<CommandResult> ValidateAsync(CancellationToken cancellationToken)
  {
    string repoRoot = RepositoryRootResolver.Resolve(null);
    RepositorySettings? settings = RepositorySettingsStore.TryLoad(repoRoot, out string? settingsError);
    if (settings is null)
    {
      return CommandResult.Fail(settingsError ?? "Could not load repository settings.");
    }

    try
    {
      await metadataRefresher.RefreshAsync(repoRoot, settings, cancellationToken);
    }
    catch (Exception ex)
    {
      return CommandResult.Fail(
        $"Could not load current project metadata from Jira: {ex.Message}",
        "Check credentials in .env and Jira connectivity, then retry.");
    }

    ArtifactLoadResult? loadResult = ArtifactRepository.LoadArtifacts(repoRoot, settings, writeErrors: false);
    if (loadResult is null)
    {
      return CommandResult.Fail("Could not load backlog artifacts.");
    }

    if (loadResult.ValidationIssues.Count == 0)
    {
      return CommandResult.Ok($"Validation passed. Checked {loadResult.Documents.Count} artifact files.");
    }

    List<string> details = loadResult.ValidationIssues
      .OrderBy(issue => issue.FilePath, StringComparer.OrdinalIgnoreCase)
      .Select(issue => $"{Path.GetRelativePath(loadResult.RepoRoot, issue.FilePath)}: {issue.Message}")
      .ToList();

    return CommandResult.Fail(
      $"Validation failed. Files checked: {loadResult.Documents.Count}. Issues found: {loadResult.ValidationIssues.Count}.",
      [.. details]);
  }
}
