using System.Text.Json;
using JiraBridge.Domain.Configuration;

namespace JiraBridge.Infrastructure.Repository;

public static class RepositoryJiraConfigurationStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
  };

  public static string GetPath(string repoRoot, RepositorySettings settings) =>
    PathResolver.ResolveRepoRelativePath(repoRoot, settings.MetadataFile);

  public static RepositoryJiraConfiguration? TryLoad(string repoRoot, RepositorySettings settings, out string? error)
  {
    string path = GetPath(repoRoot, settings);
    error = null;

    if (!File.Exists(path))
    {
      error = $"Missing Jira metadata cache: {Path.GetRelativePath(repoRoot, path)}. Run 'jirabridge configure <jira-project-key>' or retry when Jira is reachable.";
      return null;
    }

    try
    {
      RepositoryJiraConfiguration? configuration = JsonSerializer.Deserialize<RepositoryJiraConfiguration>(
        File.ReadAllText(path),
        JsonOptions);

      if (configuration is null)
      {
        error = $"Could not deserialize Jira project configuration: {Path.GetRelativePath(repoRoot, path)}";
        return null;
      }

      return configuration;
    }
    catch (Exception ex)
    {
      error = $"Could not read Jira project configuration '{Path.GetRelativePath(repoRoot, path)}': {ex.Message}";
      return null;
    }
  }

  public static void Save(string repoRoot, RepositorySettings settings, RepositoryJiraConfiguration configuration)
  {
    string path = GetPath(repoRoot, settings);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(configuration, JsonOptions) + System.Environment.NewLine);
  }
}
