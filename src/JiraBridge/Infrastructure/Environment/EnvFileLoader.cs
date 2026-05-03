namespace JiraBridge.Infrastructure.Environment;

public static class EnvFileLoader
{
  private static bool isLoaded;
  private static string? loadedRepoRoot;

  public static void LoadIfPresent(string repoRoot)
  {
    string normalizedRepoRoot = Path.GetFullPath(repoRoot);
    if (isLoaded && string.Equals(loadedRepoRoot, normalizedRepoRoot, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    isLoaded = true;
    loadedRepoRoot = normalizedRepoRoot;

    string envPath = Path.Combine(normalizedRepoRoot, ".env");
    if (!File.Exists(envPath))
    {
      return;
    }

    foreach (string rawLine in File.ReadAllLines(envPath))
    {
      string line = rawLine.Trim();
      if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
      {
        continue;
      }

      int separatorIndex = line.IndexOf('=');
      if (separatorIndex <= 0)
      {
        continue;
      }

      string key = line[..separatorIndex].Trim();
      string value = line[(separatorIndex + 1)..].Trim();

      if (string.IsNullOrWhiteSpace(key))
      {
        continue;
      }

      value = TrimWrappingQuotes(value);

      string? currentValue = System.Environment.GetEnvironmentVariable(key);
      if (string.IsNullOrWhiteSpace(currentValue))
      {
        System.Environment.SetEnvironmentVariable(key, value);
      }
    }
  }

  private static string TrimWrappingQuotes(string value)
  {
    if (value.Length >= 2 &&
        ((value.StartsWith('"') && value.EndsWith('"')) ||
         (value.StartsWith('\'') && value.EndsWith('\''))))
    {
      return value[1..^1];
    }

    return value;
  }
}
