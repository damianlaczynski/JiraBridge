namespace JiraBridge.UnitTests.Support;

public sealed class TestProcessScope : IDisposable
{
  private readonly string originalCurrentDirectory;
  private readonly Dictionary<string, string?> originalEnvironment = new(StringComparer.Ordinal);

  public TestProcessScope(string currentDirectory, params string[] environmentKeys)
  {
    originalCurrentDirectory = Environment.CurrentDirectory;
    Environment.CurrentDirectory = currentDirectory;

    foreach (string key in environmentKeys)
    {
      originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
    }
  }

  public void SetEnvironmentVariable(string key, string? value)
  {
    if (!originalEnvironment.ContainsKey(key))
    {
      originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
    }

    Environment.SetEnvironmentVariable(key, value);
  }

  public void Dispose()
  {
    Environment.CurrentDirectory = originalCurrentDirectory;

    foreach ((string key, string? value) in originalEnvironment)
    {
      Environment.SetEnvironmentVariable(key, value);
    }
  }
}
