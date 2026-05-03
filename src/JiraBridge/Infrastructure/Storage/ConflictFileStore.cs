using System.Text.Json;
using JiraBridge.Domain.Sync;

namespace JiraBridge.Infrastructure.Storage;

public static class ConflictFileStore
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
  };

  public static string GetPath(string repoRoot) =>
    Path.Combine(repoRoot, ".jirabridge", "conflicts.json");

  public static List<ConflictRecord> Load(string repoRoot)
  {
    string path = GetPath(repoRoot);
    if (!File.Exists(path))
    {
      return [];
    }

    try
    {
      List<ConflictRecord>? conflicts = JsonSerializer.Deserialize<List<ConflictRecord>>(File.ReadAllText(path), JsonOptions);
      return conflicts ?? [];
    }
    catch (JsonException exception)
    {
      throw new InvalidOperationException(
        $"Could not read conflict store '{path}'. The file contains invalid JSON.",
        exception);
    }
  }

  public static void Save(string repoRoot, IReadOnlyList<ConflictRecord> conflicts)
  {
    string path = GetPath(repoRoot);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(conflicts, JsonOptions) + System.Environment.NewLine);
  }
}
