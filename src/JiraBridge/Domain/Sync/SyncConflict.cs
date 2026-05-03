namespace JiraBridge.Domain.Sync;

public sealed record SyncConflict(
  string IssueKey,
  string RelativePath,
  string Title,
  string IssueType,
  string Operation,
  string Summary,
  string Details,
  DateTimeOffset DetectedAtUtc);
