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
    error = null;
    string resolvedPath = PathResolver.ResolveRepoRelativePath(repoRoot, settings.MetadataFile);

    if (!File.Exists(resolvedPath))
    {
      error =
        $"Missing project metadata file '{Path.GetRelativePath(repoRoot, resolvedPath)}'. Run 'configure' while Jira is reachable, then retry.";
      return null;
    }

    try
    {
      RepositoryJiraConfiguration? configuration = JsonSerializer.Deserialize<RepositoryJiraConfiguration>(
        File.ReadAllText(resolvedPath),
        JsonOptions);

      if (configuration is null)
      {
        error = $"Could not deserialize project metadata: {Path.GetRelativePath(repoRoot, resolvedPath)}";
        return null;
      }

      return configuration;
    }
    catch (Exception ex)
    {
      error = $"Could not read project metadata '{Path.GetRelativePath(repoRoot, resolvedPath)}': {ex.Message}";
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
