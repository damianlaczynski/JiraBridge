using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;
using JiraBridge.Domain.Artifacts;
using JiraBridge.Domain.Configuration;
using JiraBridge.Domain.Sync;
using JiraBridge.Infrastructure.Environment;
using JiraBridge.Infrastructure.Jira;
using JiraBridge.Infrastructure.Parsing;
using JiraBridge.Infrastructure.Repository;

namespace JiraBridge.Infrastructure.Storage;

public sealed class ConflictResolver(
  IJiraApiClientFactory jiraApiClientFactory,
  IRepositoryMetadataRefresher metadataRefresher) : IConflictResolver
{
  public async Task<CommandResult> ResolveAsync(
    string issueKey,
    ConflictResolutionStrategy strategy,
    CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    string repoRoot = RepositoryRootResolver.Resolve(null);
    RepositorySettings? repositorySettings = RepositorySettingsStore.TryLoad(repoRoot, out string? settingsError);
    if (repositorySettings is null || settingsError is not null)
    {
      return CommandResult.Fail(settingsError ?? "Could not load repository settings.");
    }
    List<ConflictRecord> conflicts = ConflictFileStore.Load(repoRoot);
    ConflictRecord? conflict = conflicts.FirstOrDefault(item => string.Equals(item.IssueKey, issueKey, StringComparison.OrdinalIgnoreCase));
    if (conflict is null)
    {
      return CommandResult.Fail($"Conflict '{issueKey}' was not found.");
    }

    try
    {
      await metadataRefresher.RefreshAsync(repoRoot, repositorySettings, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      return CommandResult.Fail(
        $"Could not load current project metadata from Jira: {ex.Message}",
        "Check credentials in .env and Jira connectivity, then retry.");
    }

    ArtifactLoadResult? loadResult = ArtifactRepository.LoadArtifacts(repoRoot, repositorySettings, writeErrors: false, allowEmptyBacklog: true);
    if (loadResult is null)
    {
      return CommandResult.Fail("Could not load repository artifacts.");
    }

    RepositoryJiraConfiguration jiraConfiguration = loadResult.JiraConfiguration
      ?? throw new InvalidOperationException("Missing project metadata after refresh.");

    string normalizedRepoRoot = Path.GetFullPath(
      loadResult.RepoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    ArtifactDocument? document = FindDocumentForConflict(loadResult, normalizedRepoRoot, conflict.RelativePath);

    if (document is null)
    {
      return CommandResult.Fail($"Conflict file '{conflict.RelativePath}' was not found in the repository.");
    }

    JiraSettings settings = JiraSettingsLoader.LoadFromEnvironment(repoRoot);
    using JiraApiClient client = jiraApiClientFactory.Create(settings);
    JiraRemoteIssue remoteIssue = await client.GetIssueAsync(
      conflict.IssueKey,
      loadResult.RepositorySettings.SprintMappingEnabled ? jiraConfiguration.SprintFieldId : null,
      cancellationToken);

    switch (strategy)
    {
      case ConflictResolutionStrategy.Repository:
        await ResolveWithRepositoryAsync(loadResult, jiraConfiguration, document, client, remoteIssue, cancellationToken);
        break;
      case ConflictResolutionStrategy.Jira:
        ResolveWithJira(loadResult, document, remoteIssue);
        break;
      case ConflictResolutionStrategy.Merge:
        await ResolveWithMergeAsync(loadResult, jiraConfiguration, document, client, remoteIssue, cancellationToken);
        break;
      default:
        return CommandResult.Fail($"Conflict strategy '{strategy}' is not supported.");
    }

    ConflictStore.Clear(repoRoot, issueKey);
    return CommandResult.Ok($"Conflict '{issueKey}' resolved using '{strategy}'.");
  }

  private static ArtifactDocument? FindDocumentForConflict(
    ArtifactLoadResult loadResult,
    string normalizedRepoRoot,
    string conflictRelativePath)
  {
    foreach (ArtifactDocument item in loadResult.Documents.Values)
    {
      if (PathResolver.AreRepositoryRelativePathsEqual(item.RelativePath(normalizedRepoRoot), conflictRelativePath))
      {
        return item;
      }
    }

    try
    {
      string candidateAbsolute = PathResolver.ResolveRepoRelativePath(normalizedRepoRoot, conflictRelativePath);
      if (loadResult.Documents.TryGetValue(candidateAbsolute, out ArtifactDocument? hit))
      {
        return hit;
      }

      string candidateFull = Path.GetFullPath(candidateAbsolute);
      foreach (ArtifactDocument doc in loadResult.Documents.Values)
      {
        if (string.Equals(Path.GetFullPath(doc.Path), candidateFull, StringComparison.OrdinalIgnoreCase))
        {
          return doc;
        }
      }
    }
    catch (ArgumentException)
    {
    }

    return null;
  }

  private static async Task ResolveWithRepositoryAsync(
    ArtifactLoadResult loadResult,
    RepositoryJiraConfiguration jiraConfiguration,
    ArtifactDocument document,
    JiraApiClient client,
    JiraRemoteIssue remoteIssue,
    CancellationToken cancellationToken)
  {
    var runtimeIssueKeys = loadResult.Documents.Values
      .Where(item => !string.IsNullOrWhiteSpace(item.JiraIssueKey))
      .ToDictionary(item => item.Path, item => item.JiraIssueKey!, StringComparer.OrdinalIgnoreCase);

    JiraIssuePayload payload = JiraPayloadMapper.Map(
      document,
      loadResult.Documents,
      runtimeIssueKeys,
      jiraConfiguration,
      loadResult.RepoRoot,
      loadResult.BacklogRoot,
      loadResult.RepositorySettings.SprintMappingEnabled);
    await client.UpdateIssueAsync(remoteIssue.IssueKey, payload, cancellationToken);
    JiraRemoteIssue updatedRemoteIssue = await client.GetIssueAsync(
      remoteIssue.IssueKey,
      loadResult.RepositorySettings.SprintMappingEnabled ? jiraConfiguration.SprintFieldId : null,
      cancellationToken);
    string localHash = ArtifactSyncStateService.ComputeLocalFingerprint(document, loadResult.BacklogRoot);
    string remoteHash = ArtifactSyncStateService.ComputeRemoteFingerprint(updatedRemoteIssue);
    ArtifactFileUpdater.WriteSyncMetadata(document.Path, remoteIssue.IssueKey, localHash, remoteHash);
    document.SetKeyValue("Metadata", "Jira Last Synced Local Hash", localHash);
    document.SetKeyValue("Metadata", "Jira Last Synced Remote Hash", remoteHash);
  }

  private static void ResolveWithJira(
    ArtifactLoadResult loadResult,
    ArtifactDocument document,
    JiraRemoteIssue remoteIssue)
  {
    var plannedPathsByIssueKey = loadResult.Documents.Values
      .Where(item => !string.IsNullOrWhiteSpace(item.JiraIssueKey))
      .ToDictionary(item => item.JiraIssueKey!, item => item.Path, StringComparer.OrdinalIgnoreCase);

    string targetPath = document.Path;
    string? parentRelativePath = ResolveParentRelativePath(targetPath, remoteIssue.ParentIssueKey, plannedPathsByIssueKey);
    IReadOnlyDictionary<string, IReadOnlyList<string>> relations = ResolveRelations(targetPath, remoteIssue, plannedPathsByIssueKey);

    ArtifactImportWriter.WriteImportedArtifact(
      targetPath,
      remoteIssue,
      parentRelativePath,
      relations);

    ArtifactDocument updatedDocument = ArtifactMarkdownParser.TryParse(targetPath, out List<string> parseErrors)
      ?? throw new InvalidOperationException($"Could not parse updated artifact '{document.RelativePath(loadResult.RepoRoot)}': {string.Join("; ", parseErrors)}");
    string localHash = ArtifactSyncStateService.ComputeLocalFingerprint(updatedDocument, loadResult.BacklogRoot);
    string remoteHash = ArtifactSyncStateService.ComputeRemoteFingerprint(remoteIssue);
    ArtifactFileUpdater.WriteSyncMetadata(targetPath, remoteIssue.IssueKey, localHash, remoteHash);
  }

  private static async Task ResolveWithMergeAsync(
    ArtifactLoadResult loadResult,
    RepositoryJiraConfiguration jiraConfiguration,
    ArtifactDocument document,
    JiraApiClient client,
    JiraRemoteIssue remoteIssue,
    CancellationToken cancellationToken)
  {
    ArtifactFileUpdater.WriteDescriptionBody(document.Path, BuildMergedDescription(document, remoteIssue));

    ArtifactDocument mergedDocument = ArtifactMarkdownParser.TryParse(document.Path, out List<string> parseErrors)
      ?? throw new InvalidOperationException($"Could not parse merged artifact '{document.RelativePath(loadResult.RepoRoot)}': {string.Join("; ", parseErrors)}");

    await ResolveWithRepositoryAsync(loadResult, jiraConfiguration, mergedDocument, client, remoteIssue, cancellationToken);
  }

  internal static string BuildMergedDescription(
    ArtifactDocument localDocument,
    JiraRemoteIssue remoteIssue)
  {
    string localDescription = localDocument.GetSectionBody("Description").Trim();
    string remoteDescription = remoteIssue.Description.Trim();

    if (string.Equals(localDescription, remoteDescription, StringComparison.Ordinal))
    {
      return localDescription;
    }

    if (remoteDescription.Contains(localDescription, StringComparison.Ordinal))
    {
      return remoteDescription;
    }

    if (localDescription.Contains(remoteDescription, StringComparison.Ordinal))
    {
      return localDescription;
    }

    if (string.IsNullOrWhiteSpace(localDescription))
    {
      return remoteDescription;
    }

    if (string.IsNullOrWhiteSpace(remoteDescription))
    {
      return localDescription;
    }

    return string.Join(
      System.Environment.NewLine,
      [
        "<<<<<<< REPOSITORY",
        localDescription,
        "=======",
        remoteDescription,
        ">>>>>>> JIRA"
      ]);
  }

  private static string? ResolveParentRelativePath(
    string targetPath,
    string? parentIssueKey,
    IReadOnlyDictionary<string, string> plannedPathsByIssueKey)
  {
    if (string.IsNullOrWhiteSpace(parentIssueKey) ||
        !plannedPathsByIssueKey.TryGetValue(parentIssueKey, out string? parentPath))
    {
      return null;
    }

    return Path.GetRelativePath(Path.GetDirectoryName(targetPath)!, parentPath);
  }

  private static IReadOnlyDictionary<string, IReadOnlyList<string>> ResolveRelations(
    string targetPath,
    JiraRemoteIssue remoteIssue,
    IReadOnlyDictionary<string, string> plannedPathsByIssueKey)
  {
    var relations = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    foreach (JiraRemoteLink link in remoteIssue.Links)
    {
      string? linkedIssueKey = link.OutwardIssueKey ?? link.InwardIssueKey;
      if (string.IsNullOrWhiteSpace(link.LinkType) ||
          string.IsNullOrWhiteSpace(linkedIssueKey) ||
          !plannedPathsByIssueKey.TryGetValue(linkedIssueKey, out string? linkedPath))
      {
        continue;
      }

      string relativePath = Path.GetRelativePath(Path.GetDirectoryName(targetPath)!, linkedPath);
      if (!relations.TryGetValue(link.LinkType, out List<string>? values))
      {
        values = [];
        relations[link.LinkType] = values;
      }

      if (!values.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
      {
        values.Add(relativePath);
      }
    }

    return relations.ToDictionary(
      pair => pair.Key,
      pair => (IReadOnlyList<string>)pair.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
      StringComparer.OrdinalIgnoreCase);
  }
}
