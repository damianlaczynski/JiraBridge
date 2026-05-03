using JiraBridge.Application.Abstractions;
using JiraBridge.Domain.Sync;

namespace JiraBridge.Infrastructure.Storage;

public sealed class ConflictStore : IConflictStore
{
  public Task<IReadOnlyCollection<SyncConflict>> GetOpenConflictsAsync(CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    string repoRoot = Repository.RepositoryRootResolver.Resolve(null);
    string storePath = ConflictFileStore.GetPath(repoRoot);
    DateTimeOffset detectedAtUtc = File.Exists(storePath)
      ? File.GetLastWriteTimeUtc(storePath)
      : DateTimeOffset.UtcNow;

    IReadOnlyCollection<SyncConflict> conflicts = ConflictFileStore.Load(repoRoot)
      .OrderBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
      .Select(record => new SyncConflict(
        record.IssueKey,
        record.RelativePath,
        record.Title,
        record.IssueType,
        record.Operation,
        BuildSummary(record),
        record.Details,
        detectedAtUtc))
      .ToArray();

    return Task.FromResult(conflicts);
  }

  public static void Record(string repoRoot, ConflictRecord conflict)
  {
    List<ConflictRecord> conflicts = ConflictFileStore.Load(repoRoot);
    int existingIndex = conflicts.FindIndex(item => string.Equals(item.IssueKey, conflict.IssueKey, StringComparison.OrdinalIgnoreCase));
    if (existingIndex >= 0)
    {
      conflicts[existingIndex] = conflict;
    }
    else
    {
      conflicts.Add(conflict);
    }

    ConflictFileStore.Save(repoRoot, conflicts);
  }

  public static void Clear(string repoRoot, string issueKey)
  {
    List<ConflictRecord> conflicts = ConflictFileStore.Load(repoRoot);
    int removed = conflicts.RemoveAll(item => string.Equals(item.IssueKey, issueKey, StringComparison.OrdinalIgnoreCase));
    if (removed == 0 && !File.Exists(ConflictFileStore.GetPath(repoRoot)))
    {
      return;
    }

    ConflictFileStore.Save(repoRoot, conflicts);
  }
  private static string BuildSummary(ConflictRecord record)
  {
    string issueType = string.IsNullOrWhiteSpace(record.IssueType) ? "unknown" : record.IssueType;
    string title = string.IsNullOrWhiteSpace(record.Title) ? record.IssueKey : record.Title;
    return $"{record.Operation} [{issueType}] {title}";
  }
}
