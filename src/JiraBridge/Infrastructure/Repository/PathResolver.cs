namespace JiraBridge.Infrastructure.Repository;

public static class PathResolver
{
  public static string ResolveRepoRelativePath(string repoRoot, string relativePath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
    ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

    string fullRepoRoot = EnsureTrailingSeparator(Path.GetFullPath(repoRoot));
    string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
    string resolvedPath = Path.GetFullPath(Path.Combine(fullRepoRoot, normalizedRelativePath));

    if (!resolvedPath.StartsWith(fullRepoRoot, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException(
        $"Resolved path escapes repository root. Repo root: '{repoRoot}', relative path: '{relativePath}'.");
    }

    return resolvedPath;
  }

  public static string ResolveArtifactRelativePath(string artifactPath, string relativePath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
    ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

    string artifactDirectory = Path.GetDirectoryName(Path.GetFullPath(artifactPath))
      ?? throw new InvalidOperationException($"Could not resolve directory for artifact path '{artifactPath}'.");

    return Path.GetFullPath(Path.Combine(artifactDirectory, relativePath));
  }

  public static bool IsNone(string value) =>
    string.Equals(value.Trim(), "none", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(value.Trim(), "brak", StringComparison.OrdinalIgnoreCase);

  private static string EnsureTrailingSeparator(string path) =>
    path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
