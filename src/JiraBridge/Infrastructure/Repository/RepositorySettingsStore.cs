using System.Text.Json;
using JiraBridge.Domain.Configuration;

namespace JiraBridge.Infrastructure.Repository;

public static class RepositorySettingsStore
{
  private const string SettingsFileName = "settings.json";
  private const string LegacySettingsFileName = ".jirabridge.json";

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
  };

  public static RepositorySettings CreateDefault(string jiraProjectKey) =>
    new(
      SchemaVersion: 1,
      JiraProjectKey: jiraProjectKey,
      BacklogRoot: RepositoryLayout.Default.BacklogRoot,
      MetadataFile: RepositoryLayout.Default.JiraMetadataFile,
      SprintMappingEnabled: true);

  public static string GetPath(string repoRoot) =>
    PathResolver.ResolveRepoRelativePath(repoRoot, RepositoryLayout.Default.SettingsFile);

  public static RepositorySettings? TryLoad(string repoRoot, out string? error)
  {
    string path = GetPath(repoRoot);
    string legacyPath = PathResolver.ResolveRepoRelativePath(repoRoot, LegacySettingsFileName);
    error = null;

    if (!File.Exists(path) && !File.Exists(legacyPath))
    {
      error = $"Missing repository settings: {Path.GetRelativePath(repoRoot, path)}. Run 'jirabridge configure <jira-project-key>'.";
      return null;
    }

    string filePath = File.Exists(path) ? path : legacyPath;

    try
    {
      RepositorySettings? settings = JsonSerializer.Deserialize<RepositorySettings>(
        File.ReadAllText(filePath),
        JsonOptions);

      if (settings is null)
      {
        error = $"Could not deserialize repository settings: {Path.GetRelativePath(repoRoot, filePath)}.";
        return null;
      }

      return settings;
    }
    catch (Exception ex)
    {
      error = $"Could not read repository settings '{Path.GetRelativePath(repoRoot, filePath)}': {ex.Message}";
      return null;
    }
  }

  public static void Save(string repoRoot, RepositorySettings settings)
  {
    string path = GetPath(repoRoot);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions) + System.Environment.NewLine);
  }
}
