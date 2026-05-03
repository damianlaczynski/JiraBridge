namespace JiraBridge.Domain.Sync;

public sealed record ConflictRecord(
  string IssueKey,
  string RelativePath,
  string Operation,
  string Title,
  string IssueType,
  string LocalHash,
  string RemoteHash,
  string Details);
