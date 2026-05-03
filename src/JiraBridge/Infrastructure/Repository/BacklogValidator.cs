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

    string? refreshWarning = null;
    try
    {
      await metadataRefresher.RefreshAsync(repoRoot, settings, cancellationToken);
    }
    catch (Exception ex)
    {
      refreshWarning = $"Warning: could not refresh Jira metadata. Falling back to the local cache. Details: {ex.Message}";
    }

    ArtifactLoadResult? loadResult = ArtifactRepository.LoadArtifacts(repoRoot, settings, writeErrors: false);
    if (loadResult is null)
    {
      return CommandResult.Fail("Could not load backlog artifacts.");
    }

    if (loadResult.ValidationIssues.Count == 0)
    {
      return string.IsNullOrWhiteSpace(refreshWarning)
        ? CommandResult.Ok($"Validation passed. Checked {loadResult.Documents.Count} artifact files.")
        : CommandResult.Ok($"Validation passed. Checked {loadResult.Documents.Count} artifact files.", refreshWarning);
    }

    List<string> details = loadResult.ValidationIssues
      .OrderBy(issue => issue.FilePath, StringComparer.OrdinalIgnoreCase)
      .Select(issue => $"{Path.GetRelativePath(loadResult.RepoRoot, issue.FilePath)}: {issue.Message}")
      .ToList();

    if (!string.IsNullOrWhiteSpace(refreshWarning))
    {
      details.Insert(0, refreshWarning);
    }

    return CommandResult.Fail(
      $"Validation failed. Files checked: {loadResult.Documents.Count}. Issues found: {loadResult.ValidationIssues.Count}.",
      [.. details]);
  }
}
