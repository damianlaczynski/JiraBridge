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

public sealed class SyncExecutor(
  IJiraApiClientFactory jiraApiClientFactory,
  IRepositoryMetadataRefresher metadataRefresher,
  IOperationProgressSink progressSink) : ISyncExecutor
{
  public async Task<CommandResult> PullAsync(CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    progressSink.Start("Pull", "Inspecting repository state...", totalSteps: 6);

    string repoRoot = RepositoryRootResolver.Resolve(null);
    RepositorySettings? repositorySettings = RepositorySettingsStore.TryLoad(repoRoot, out string? settingsError);
    if (repositorySettings is null || settingsError is not null)
    {
      progressSink.Fail(settingsError ?? "Could not load repository settings.");
      return CommandResult.Fail(settingsError ?? "Could not load repository settings.");
    }
    progressSink.ReportStep("Validated repository settings.");

    ArtifactLoadResult? loadResult = ArtifactRepository.LoadArtifacts(repoRoot, repositorySettings, writeErrors: false, allowEmptyBacklog: true);
    if (loadResult is null)
    {
      progressSink.Fail("Could not load repository artifacts.");
      return CommandResult.Fail("Could not load repository artifacts.");
    }
    progressSink.ReportStep($"Loaded {loadResult.Documents.Count} local artifact(s).");

    RepositoryJiraConfiguration? jiraConfiguration = loadResult.JiraConfiguration;
    if (jiraConfiguration is null)
    {
      string metadataPath = RepositoryJiraConfigurationStore.GetPath(repoRoot, repositorySettings);
      progressSink.Fail($"Missing Jira metadata cache: {Path.GetRelativePath(repoRoot, metadataPath)}.");
      return CommandResult.Fail(
        $"Missing Jira metadata cache: {Path.GetRelativePath(repoRoot, metadataPath)}.",
        "Run configure first or retry when Jira is reachable.");
    }
    progressSink.ReportStep($"Loaded Jira metadata cache for project '{jiraConfiguration.ProjectKey}'.");
    jiraConfiguration = await EnsureSprintProjectionAsync(repoRoot, repositorySettings, jiraConfiguration, cancellationToken).ConfigureAwait(false);

    JiraSettings settings = JiraSettingsLoader.LoadFromEnvironment(repoRoot);
    using JiraApiClient client = jiraApiClientFactory.Create(settings);

    IReadOnlyList<JiraRemoteIssue> remoteIssues = await client.SearchProjectIssuesAsync(
      jiraConfiguration.ProjectKey,
      repositorySettings.SprintMappingEnabled ? jiraConfiguration.SprintFieldId : null,
      cancellationToken);
    progressSink.ReportStep($"Fetched {remoteIssues.Count} issue(s) from Jira.");

    var existingByIssueKey = loadResult.Documents.Values
      .Where(document => !string.IsNullOrWhiteSpace(document.JiraIssueKey))
      .ToDictionary(document => document.JiraIssueKey!, document => document, StringComparer.OrdinalIgnoreCase);

    var remoteIssuesByKey = remoteIssues.ToDictionary(issue => issue.IssueKey, StringComparer.OrdinalIgnoreCase);
    var plannedPathsByIssueKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (JiraRemoteIssue remoteIssue in remoteIssues)
    {
      plannedPathsByIssueKey[remoteIssue.IssueKey] = ResolvePlannedArtifactPath(
        remoteIssue,
        existingByIssueKey,
        remoteIssuesByKey,
        plannedPathsByIssueKey,
        loadResult.BacklogRoot);
    }

    int importedCount = 0;
    foreach (JiraRemoteIssue remoteIssue in remoteIssues)
    {
      if (existingByIssueKey.ContainsKey(remoteIssue.IssueKey))
      {
        continue;
      }

      string targetPath = plannedPathsByIssueKey[remoteIssue.IssueKey];
      string? parentRelativePath = ResolveParentRelativePath(targetPath, remoteIssue.ParentIssueKey, plannedPathsByIssueKey);
      IReadOnlyDictionary<string, IReadOnlyList<string>> relations = ResolveRelations(targetPath, remoteIssue, plannedPathsByIssueKey);

      ArtifactImportWriter.WriteImportedArtifact(
        targetPath,
        remoteIssue,
        parentRelativePath,
        relations);

      ArtifactDocument importedDocument = ArtifactMarkdownParser.TryParse(targetPath, out List<string> importErrors)
        ?? throw new InvalidOperationException(
          $"Could not parse imported artifact '{Path.GetRelativePath(repoRoot, targetPath)}': {string.Join("; ", importErrors)}");

      string localHash = ArtifactSyncStateService.ComputeLocalFingerprint(importedDocument);
      string remoteHash = ArtifactSyncStateService.ComputeRemoteFingerprint(remoteIssue);
      ArtifactFileUpdater.WriteSyncMetadata(targetPath, remoteIssue.IssueKey, localHash, remoteHash);
      importedCount++;
    }
    progressSink.ReportStep($"Imported {importedCount} new artifact(s) from Jira.");

    ArtifactLoadResult? refreshedLoadResult = ArtifactRepository.LoadArtifacts(repoRoot, repositorySettings, writeErrors: false, allowEmptyBacklog: true);
    if (refreshedLoadResult is null)
    {
      progressSink.Fail("Could not reload artifacts after Jira pull.");
      return CommandResult.Fail("Could not reload artifacts after Jira pull.");
    }

    int updatedCount = 0;
    int conflictCount = 0;
    var movedArtifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (ArtifactDocument document in refreshedLoadResult.Documents.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
    {
      if (string.IsNullOrWhiteSpace(document.JiraIssueKey))
      {
        continue;
      }

      if (!remoteIssuesByKey.TryGetValue(document.JiraIssueKey, out JiraRemoteIssue? remoteIssue))
      {
        continue;
      }

      bool localChanged = ArtifactSyncStateService.HasLocalChanges(document);
      bool remoteChanged = ArtifactSyncStateService.HasRemoteChanges(document, remoteIssue);

      if (!(localChanged && remoteChanged))
      {
        ConflictStore.Clear(repoRoot, remoteIssue.IssueKey);
      }

      if (!remoteChanged)
      {
        continue;
      }

      if (localChanged)
      {
        conflictCount++;
        JiraIssuePayload localPayload = JiraPayloadMapper.Map(
          document,
          refreshedLoadResult.Documents,
          refreshedLoadResult.Documents.Values
            .Where(item => !string.IsNullOrWhiteSpace(item.JiraIssueKey))
            .ToDictionary(item => item.Path, item => item.JiraIssueKey!, StringComparer.OrdinalIgnoreCase),
          jiraConfiguration,
          repoRoot,
          refreshedLoadResult.BacklogRoot,
          repositorySettings.SprintMappingEnabled);

        ConflictStore.Record(
          repoRoot,
          new ConflictRecord(
            remoteIssue.IssueKey,
            document.RelativePath(repoRoot),
            "pull",
            document.Title,
            document.JiraIssueType ?? remoteIssue.IssueType,
            ArtifactSyncStateService.ComputeLocalFingerprint(document),
            ArtifactSyncStateService.ComputeRemoteFingerprint(remoteIssue),
            ConflictDiffFormatter.Build(document, localPayload, remoteIssue)));
        continue;
      }

      string targetPath = plannedPathsByIssueKey.TryGetValue(remoteIssue.IssueKey, out string? plannedPath)
        ? plannedPath
        : document.Path;
      string? parentRelativePath = ResolveParentRelativePath(targetPath, remoteIssue.ParentIssueKey, plannedPathsByIssueKey);
      IReadOnlyDictionary<string, IReadOnlyList<string>> relations = ResolveRelations(targetPath, remoteIssue, plannedPathsByIssueKey);

      ArtifactImportWriter.WriteImportedArtifact(
        targetPath,
        remoteIssue,
        parentRelativePath,
        relations);

      ArtifactDocument updatedDocument = ArtifactMarkdownParser.TryParse(targetPath, out List<string> parseErrors)
        ?? throw new InvalidOperationException(
          $"Could not parse updated artifact '{document.RelativePath(repoRoot)}': {string.Join("; ", parseErrors)}");
      string localHash = ArtifactSyncStateService.ComputeLocalFingerprint(updatedDocument);
      string remoteHash = ArtifactSyncStateService.ComputeRemoteFingerprint(remoteIssue);
      ArtifactFileUpdater.WriteSyncMetadata(targetPath, remoteIssue.IssueKey, localHash, remoteHash);
      if (!string.Equals(targetPath, document.Path, StringComparison.OrdinalIgnoreCase))
      {
        movedArtifacts[document.Path] = targetPath;
      }

      updatedCount++;
    }

    RemoveMovedArtifactSourceFiles(movedArtifacts);

    if (movedArtifacts.Count > 0)
    {
      ArtifactLoadResult? postMoveLoadResult = ArtifactRepository.LoadArtifacts(repoRoot, repositorySettings, writeErrors: false, allowEmptyBacklog: true);
      if (postMoveLoadResult is null)
      {
        progressSink.Fail("Could not reload artifacts after sprint-based file relocation.");
        return CommandResult.Fail("Could not reload artifacts after sprint-based file relocation.");
      }

      RewriteReferencesAfterMove(postMoveLoadResult.Documents.Values, movedArtifacts);
    }

    progressSink.ReportStep($"Applied remote updates and recorded {conflictCount} conflict(s).");
    string pullMessage = $"Pull complete. Imported {importedCount} new artifacts, updated {updatedCount} local artifacts, detected {conflictCount} conflicts.";
    progressSink.Complete(pullMessage);
    return CommandResult.Ok(
      pullMessage,
      $"[INFO] Jira issues scanned: {remoteIssues.Count}",
      $"[INFO] Imported artifacts: {importedCount}",
      $"[INFO] Updated artifacts: {updatedCount}",
      conflictCount == 0
        ? "[OK] No pull conflicts detected."
        : $"[WARN] Pull conflicts recorded: {conflictCount}");
  }

  public async Task<CommandResult> PushAsync(bool dryRun, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    progressSink.Start(
      dryRun ? "Push Dry-Run" : "Push",
      dryRun ? "Preparing dry-run preview..." : "Preparing push...",
      totalSteps: 7);

    string repoRoot = RepositoryRootResolver.Resolve(null);
    RepositorySettings? repositorySettings = RepositorySettingsStore.TryLoad(repoRoot, out string? settingsError);
    if (repositorySettings is null || settingsError is not null)
    {
      progressSink.Fail(settingsError ?? "Could not load repository settings.");
      return CommandResult.Fail(settingsError ?? "Could not load repository settings.");
    }
    progressSink.ReportStep("Validated repository settings.");

    ArtifactLoadResult? loadResult = ArtifactRepository.LoadArtifacts(repoRoot, repositorySettings, writeErrors: false, allowEmptyBacklog: true);
    if (loadResult is null)
    {
      progressSink.Fail("Could not load repository artifacts.");
      return CommandResult.Fail("Could not load repository artifacts.");
    }
    progressSink.ReportStep($"Loaded {loadResult.Documents.Count} artifact(s) from the backlog.");

    RepositoryJiraConfiguration? jiraConfiguration = loadResult.JiraConfiguration;
    if (jiraConfiguration is null)
    {
      string metadataPath = RepositoryJiraConfigurationStore.GetPath(repoRoot, repositorySettings);
      progressSink.Fail($"Missing Jira metadata cache: {Path.GetRelativePath(repoRoot, metadataPath)}.");
      return CommandResult.Fail(
        $"Missing Jira metadata cache: {Path.GetRelativePath(repoRoot, metadataPath)}.",
        "Run configure first or retry when Jira is reachable.");
    }
    progressSink.ReportStep($"Loaded Jira metadata cache for project '{jiraConfiguration.ProjectKey}'.");
    jiraConfiguration = await EnsureSprintProjectionAsync(repoRoot, repositorySettings, jiraConfiguration, cancellationToken).ConfigureAwait(false);

    JiraSettings settings = JiraSettingsLoader.LoadFromEnvironment(repoRoot);
    List<ArtifactDocument> orderedDocuments = PlanBuilder.OrderDocuments(loadResult.Documents.Values.ToList());
    var runtimeIssueKeys = orderedDocuments
      .Where(document => !string.IsNullOrWhiteSpace(document.JiraIssueKey))
      .ToDictionary(document => document.Path, document => document.JiraIssueKey!, StringComparer.OrdinalIgnoreCase);

    using JiraApiClient client = jiraApiClientFactory.Create(settings);

    List<PushCandidate> candidates = await BuildCandidatesAsync(
      orderedDocuments,
      loadResult,
      jiraConfiguration,
      runtimeIssueKeys,
      repositorySettings.SprintMappingEnabled,
      client,
      cancellationToken);
    progressSink.ReportStep("Compared local artifacts with Jira and prepared push actions.");

    List<PushCandidate> conflicts = candidates.Where(candidate => candidate.Operation == PushOperation.Conflict).ToList();
    List<PushCandidate> actionableCandidates = candidates.Where(candidate => candidate.ShouldPush).ToList();
    int createCount = candidates.Count(candidate => candidate.Operation == PushOperation.Create);
    int updateCount = candidates.Count(candidate => candidate.Operation == PushOperation.Update);
    int skippedCount = candidates.Count(candidate => candidate.Operation == PushOperation.Skip);

    foreach (PushCandidate candidate in conflicts)
    {
      if (dryRun)
      {
        continue;
      }

      ConflictStore.Record(repoRoot, candidate.ToConflictRecord());
    }

    if (actionableCandidates.Count == 0)
    {
      progressSink.ReportStep("No Jira write actions are needed.");
      string noActionMessage = conflicts.Count == 0
        ? "No local changes detected. Nothing to push."
        : $"No push actions were executed. Recorded {conflicts.Count} conflict(s).";
      progressSink.Complete(noActionMessage);
      return CommandResult.Ok(
        noActionMessage,
        $"[INFO] Artifacts evaluated: {candidates.Count}",
        $"[INFO] Creates: {createCount}",
        $"[INFO] Updates: {updateCount}",
        $"[INFO] Unchanged: {skippedCount}",
        conflicts.Count == 0
          ? "[OK] No conflicts detected."
          : $"[WARN] Conflicts recorded: {conflicts.Count}",
        dryRun
          ? "[INFO] Dry-run preview mode: no Jira write operations were executed."
          : "[INFO] Repository state stayed unchanged because nothing actionable was found.");
    }

    progressSink.ReportStep(dryRun
      ? "Previewing Jira issue writes."
      : "Publishing create and update operations to Jira.");
    foreach (PushCandidate candidate in actionableCandidates)
    {
      if (dryRun)
      {
        continue;
      }

      JiraIssuePayload executionPayload = JiraPayloadMapper.Map(
        candidate.Document,
        loadResult.Documents,
        runtimeIssueKeys,
        jiraConfiguration,
        repoRoot,
        loadResult.BacklogRoot,
        repositorySettings.SprintMappingEnabled);

      if (candidate.IsCreate)
      {
        string createdIssueKey = await client.CreateIssueAsync(executionPayload, cancellationToken);
        JiraRemoteIssue createdIssue = await client.GetIssueAsync(
          createdIssueKey,
          repositorySettings.SprintMappingEnabled ? jiraConfiguration.SprintFieldId : null,
          cancellationToken);
        runtimeIssueKeys[candidate.Document.Path] = createdIssueKey;
        PersistSyncState(
          candidate.Document,
          createdIssueKey,
          candidate.LocalHash,
          ArtifactSyncStateService.ComputeRemoteFingerprint(createdIssue));
      }
      else
      {
        await client.UpdateIssueAsync(executionPayload.ExistingIssueKey!, executionPayload, cancellationToken);
        JiraRemoteIssue updatedIssue = await client.GetIssueAsync(
          executionPayload.ExistingIssueKey!,
          repositorySettings.SprintMappingEnabled ? jiraConfiguration.SprintFieldId : null,
          cancellationToken);
        runtimeIssueKeys[candidate.Document.Path] = executionPayload.ExistingIssueKey!;
        PersistSyncState(
          candidate.Document,
          executionPayload.ExistingIssueKey!,
          candidate.LocalHash,
          ArtifactSyncStateService.ComputeRemoteFingerprint(updatedIssue));
        ConflictStore.Clear(repoRoot, executionPayload.ExistingIssueKey!);
      }
    }

    progressSink.ReportStep(dryRun
      ? "Previewing relationship synchronization."
      : "Synchronizing Jira issue relationships.");
    int relationshipCount = 0;
    foreach (PushCandidate candidate in actionableCandidates)
    {
      if (!runtimeIssueKeys.TryGetValue(candidate.Document.Path, out string? sourceIssueKey))
      {
        continue;
      }

      foreach ((string relationName, string relationPath) in GetArtifactRelationships(candidate.Document, loadResult.Documents))
      {
        string resolvedPath = PathResolver.ResolveArtifactRelativePath(candidate.Document.Path, relationPath);
        if (!runtimeIssueKeys.TryGetValue(resolvedPath, out string? targetIssueKey))
        {
          continue;
        }

        if (dryRun)
        {
          relationshipCount++;
          continue;
        }

        await client.EnsureIssueLinkAsync(relationName, sourceIssueKey, targetIssueKey, cancellationToken);
        relationshipCount++;
      }
    }

    string message = dryRun
      ? $"Push dry-run complete. Actionable artifacts: {actionableCandidates.Count}. Conflicts: {conflicts.Count}."
      : $"Push complete. Updated artifacts: {actionableCandidates.Count}. Conflicts: {conflicts.Count}.";
    progressSink.ReportStep(dryRun
      ? "Dry-run completed without mutating Jira."
      : "Push write operations completed.");
    progressSink.Complete(message);

    string[] details =
    [
      $"[INFO] Artifacts evaluated: {candidates.Count}",
      $"[INFO] Creates: {createCount}",
      $"[INFO] Updates: {updateCount}",
      $"[INFO] Unchanged: {skippedCount}",
      conflicts.Count == 0
        ? "[OK] Validation passed: no conflicts detected."
        : $"[WARN] Conflicts recorded: {conflicts.Count}",
      $"[INFO] Relationship actions: {relationshipCount}",
      dryRun
        ? "[INFO] Dry-run preview mode: Jira write operations were skipped."
        : "[OK] Jira write operations finished successfully."
    ];

    return CommandResult.Ok(message, details);
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

  private static string ResolvePlannedArtifactPath(
    JiraRemoteIssue remoteIssue,
    IReadOnlyDictionary<string, ArtifactDocument> existingByIssueKey,
    IReadOnlyDictionary<string, JiraRemoteIssue> remoteIssuesByKey,
    IDictionary<string, string> plannedPathsByIssueKey,
    string backlogRoot)
  {
    if (plannedPathsByIssueKey.TryGetValue(remoteIssue.IssueKey, out string? plannedPath))
    {
      return plannedPath;
    }

    string? parentPath = null;
    if (!string.IsNullOrWhiteSpace(remoteIssue.ParentIssueKey))
    {
      if (remoteIssuesByKey.TryGetValue(remoteIssue.ParentIssueKey, out JiraRemoteIssue? parentIssue))
      {
        parentPath = ResolvePlannedArtifactPath(
          parentIssue,
          existingByIssueKey,
          remoteIssuesByKey,
          plannedPathsByIssueKey,
          backlogRoot);
      }
      else if (existingByIssueKey.TryGetValue(remoteIssue.ParentIssueKey, out ArtifactDocument? existingParent))
      {
        parentPath = existingParent.Path;
      }
    }

    string resolvedPath = ArtifactImportWriter.BuildPlannedArtifactPath(backlogRoot, remoteIssue, parentPath);
    if (existingByIssueKey.TryGetValue(remoteIssue.IssueKey, out ArtifactDocument? existingDocument))
    {
      if (ShouldKeepExistingPath(existingDocument.Path, parentPath, remoteIssue.Sprint))
      {
        plannedPathsByIssueKey[remoteIssue.IssueKey] = existingDocument.Path;
        return existingDocument.Path;
      }
    }

    plannedPathsByIssueKey[remoteIssue.IssueKey] = resolvedPath;
    return resolvedPath;
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

  private static async Task<List<PushCandidate>> BuildCandidatesAsync(
    IReadOnlyList<ArtifactDocument> orderedDocuments,
    ArtifactLoadResult loadResult,
    RepositoryJiraConfiguration jiraConfiguration,
    IReadOnlyDictionary<string, string> runtimeIssueKeys,
    bool sprintMappingEnabled,
    JiraApiClient client,
    CancellationToken cancellationToken)
  {
    var candidates = new List<PushCandidate>();

    foreach (ArtifactDocument document in orderedDocuments)
    {
      PlanItem planItem = PlanBuilder.CreatePlanItem(document, loadResult.RepoRoot, jiraConfiguration.ProjectKey);
      JiraIssuePayload payload = JiraPayloadMapper.Map(
        document,
        loadResult.Documents,
        runtimeIssueKeys,
        jiraConfiguration,
        loadResult.RepoRoot,
        loadResult.BacklogRoot,
        sprintMappingEnabled);
      string localHash = ArtifactSyncStateService.ComputeLocalFingerprint(document);

      if (string.IsNullOrWhiteSpace(payload.ExistingIssueKey))
      {
        candidates.Add(new PushCandidate(document, planItem, payload, localHash, null, PushOperation.Create, null, null, true, false));
        continue;
      }

      JiraRemoteIssue remoteIssue = await client.GetIssueAsync(
        payload.ExistingIssueKey,
        sprintMappingEnabled ? jiraConfiguration.SprintFieldId : null,
        cancellationToken);
      string remoteHash = ArtifactSyncStateService.ComputeRemoteFingerprint(remoteIssue);
      bool localChanged = ArtifactSyncStateService.HasLocalChanges(document);
      bool remoteChanged = ArtifactSyncStateService.HasRemoteChanges(document, remoteIssue);

      if (localChanged && remoteChanged)
      {
        string conflictDetails = ConflictDiffFormatter.Build(document, payload, remoteIssue);
        candidates.Add(new PushCandidate(
          document,
          planItem,
          payload,
          localHash,
          remoteHash,
          PushOperation.Conflict,
          $"Both the repository artifact and Jira issue '{payload.ExistingIssueKey}' changed since the last sync.",
          conflictDetails,
          localChanged,
          remoteChanged));
        continue;
      }

      candidates.Add(new PushCandidate(
        document,
        planItem,
        payload,
        localHash,
        remoteHash,
        localChanged ? PushOperation.Update : PushOperation.Skip,
        null,
        null,
        localChanged,
        remoteChanged));
    }

    return candidates;
  }

  private static void PersistSyncState(
    ArtifactDocument document,
    string issueKey,
    string localHash,
    string remoteHash)
  {
    ArtifactFileUpdater.WriteSyncMetadata(document.Path, issueKey, localHash, remoteHash);
    document.SetKeyValue("Metadata", "Jira Issue Key", issueKey);
    document.SetKeyValue("Metadata", "Jira Last Synced Local Hash", localHash);
    document.SetKeyValue("Metadata", "Jira Last Synced Remote Hash", remoteHash);
  }

  private static IEnumerable<(string RelationName, string RelationPath)> GetArtifactRelationships(
    ArtifactDocument document,
    IReadOnlyDictionary<string, ArtifactDocument> documents)
  {
    if (!document.Sections.TryGetValue("Relations", out SectionContent? relations))
    {
      yield break;
    }

    foreach (KeyValuePair<string, List<string>> relation in relations.NestedLists)
    {
      foreach (string item in relation.Value.Where(item => !PathResolver.IsNone(item) && item.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
      {
        string resolvedPath = PathResolver.ResolveArtifactRelativePath(document.Path, item);
        if (!documents.ContainsKey(resolvedPath))
        {
          continue;
        }

        yield return (relation.Key, item);
      }
    }
  }

  private static bool ShouldKeepExistingPath(string existingPath, string? parentPath, JiraSprintInfo? sprint)
  {
    if (!string.IsNullOrWhiteSpace(parentPath))
    {
      string expectedDirectory = Path.Combine(
        Path.GetDirectoryName(parentPath)!,
        Path.GetFileNameWithoutExtension(parentPath)!);
      return string.Equals(
        Path.GetDirectoryName(existingPath),
        expectedDirectory,
        StringComparison.OrdinalIgnoreCase);
    }

    string? existingSprintDirectory = SprintPathConvention.TryExtractSprintDirectoryNameFromPath(existingPath);
    string? expectedSprintDirectory = sprint is null
      ? null
      : SprintPathConvention.ToSprintDirectoryName(sprint.Name);

    return string.Equals(existingSprintDirectory, expectedSprintDirectory, StringComparison.OrdinalIgnoreCase);
  }

  private static void RemoveMovedArtifactSourceFiles(IReadOnlyDictionary<string, string> movedArtifacts)
  {
    foreach ((string sourcePath, string targetPath) in movedArtifacts)
    {
      if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (File.Exists(sourcePath))
      {
        File.Delete(sourcePath);
      }
    }
  }

  private static void RewriteReferencesAfterMove(
    IEnumerable<ArtifactDocument> documents,
    IReadOnlyDictionary<string, string> movedArtifacts)
  {
    foreach (ArtifactDocument document in documents)
    {
      bool changed = false;

      string? parent = document.Parent;
      if (!string.IsNullOrWhiteSpace(parent) && !PathResolver.IsNone(parent))
      {
        string resolvedParentPath = PathResolver.ResolveArtifactRelativePath(document.Path, parent);
        if (movedArtifacts.TryGetValue(resolvedParentPath, out string? movedParentPath))
        {
          string updatedParent = Path.GetRelativePath(Path.GetDirectoryName(document.Path)!, movedParentPath);
          document.SetKeyValue("Links", "Parent", updatedParent);
          changed = true;
        }
      }

      if (document.Sections.TryGetValue("Relations", out SectionContent? relations))
      {
        foreach ((string relationName, List<string> values) in relations.NestedLists)
        {
          for (int index = 0; index < values.Count; index++)
          {
            string value = values[index];
            if (PathResolver.IsNone(value) || !value.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
              continue;
            }

            string resolvedRelationPath = PathResolver.ResolveArtifactRelativePath(document.Path, value);
            if (!movedArtifacts.TryGetValue(resolvedRelationPath, out string? movedRelationPath))
            {
              continue;
            }

            values[index] = Path.GetRelativePath(Path.GetDirectoryName(document.Path)!, movedRelationPath);
            changed = true;
          }
        }
      }

      if (!changed)
      {
        continue;
      }

      ArtifactMarkdownWriter.Write(document.Path, document);
      ArtifactDocument rewrittenDocument = ArtifactMarkdownParser.TryParse(document.Path, out List<string> parseErrors)
        ?? throw new InvalidOperationException($"Could not parse rewritten artifact '{document.Path}': {string.Join("; ", parseErrors)}");
      string localHash = ArtifactSyncStateService.ComputeLocalFingerprint(rewrittenDocument);
      ArtifactFileUpdater.WriteSyncMetadata(document.Path, document.JiraIssueKey, localHash, document.JiraLastSyncedRemoteHash ?? string.Empty);
    }
  }

  private Task<RepositoryJiraConfiguration> EnsureSprintProjectionAsync(
    string repoRoot,
    RepositorySettings repositorySettings,
    RepositoryJiraConfiguration configuration,
    CancellationToken cancellationToken)
  {
    if (!RepositoryMetadataRefresher.ShouldRefreshSprintProjection(repositorySettings, configuration))
    {
      return Task.FromResult(configuration);
    }

    progressSink.ReportStep("Refreshing Jira metadata cache (sprint projection)...");
    return metadataRefresher.RefreshAsync(repoRoot, repositorySettings, cancellationToken);
  }
}

internal enum PushOperation
{
  Skip,
  Create,
  Update,
  Conflict
}

internal sealed record PushCandidate(
  ArtifactDocument Document,
  PlanItem PlanItem,
  JiraIssuePayload Payload,
  string LocalHash,
  string? RemoteHash,
  PushOperation Operation,
  string? ConflictMessage,
  string? ConflictDetails,
  bool LocalChanged,
  bool RemoteChanged)
{
  public bool IsCreate => Operation == PushOperation.Create;

  public bool ShouldPush => Operation is PushOperation.Create or PushOperation.Update;

  public string DecisionMessage => Operation switch
  {
    PushOperation.Create => $"create {Payload.Summary}",
    PushOperation.Update => $"update {Payload.ExistingIssueKey}",
    PushOperation.Skip => $"skip {Payload.Summary}",
    PushOperation.Conflict => $"conflict {Payload.ExistingIssueKey}",
    _ => "skip"
  };

  public ConflictRecord ToConflictRecord() =>
    new(
      Payload.ExistingIssueKey ?? string.Empty,
      PlanItem.RelativePath,
      "push",
      Payload.Summary,
      Payload.IssueType,
      LocalHash,
      RemoteHash ?? string.Empty,
      ConflictDetails ?? ConflictMessage ?? string.Empty);
}
