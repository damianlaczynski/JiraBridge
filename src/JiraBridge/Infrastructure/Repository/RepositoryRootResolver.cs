namespace JiraBridge.Infrastructure.Repository;

public static class RepositoryRootResolver
{
  public static string Resolve(string? candidate)
  {
    string startPath = string.IsNullOrWhiteSpace(candidate)
      ? System.Environment.CurrentDirectory
      : candidate;

    string fullStartPath = Path.GetFullPath(startPath);
    string directoryPath = Directory.Exists(fullStartPath)
      ? fullStartPath
      : Path.GetDirectoryName(fullStartPath) ?? System.Environment.CurrentDirectory;

    var directory = new DirectoryInfo(directoryPath);
    while (directory is not null)
    {
      if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
          File.Exists(Path.Combine(directory.FullName, ".git")))
      {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    throw new InvalidOperationException(
      $"Git repository root could not be found from '{fullStartPath}'. Run the command inside a Git repository or pass a path within one.");
  }
}
