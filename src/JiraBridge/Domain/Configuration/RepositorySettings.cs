namespace JiraBridge.Domain.Configuration;

public sealed record RepositorySettings(
  int SchemaVersion,
  string JiraProjectKey,
  string BacklogRoot,
  string MetadataFile,
  bool SprintMappingEnabled = true);
