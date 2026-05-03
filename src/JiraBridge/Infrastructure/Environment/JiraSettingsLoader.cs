namespace JiraBridge.Infrastructure.Environment;

public static class JiraSettingsLoader
{
  public static JiraSettings LoadFromEnvironment(string repoRoot)
  {
    EnvFileLoader.LoadIfPresent(repoRoot);

    string baseUrl = GetRequired("JIRABRIDGE_JIRA_BASE_URL", repoRoot);
    string email = GetRequired("JIRABRIDGE_JIRA_EMAIL", repoRoot);
    string apiToken = GetRequired("JIRABRIDGE_JIRA_API_TOKEN", repoRoot);

    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri) ||
        !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException("Environment variable JIRABRIDGE_JIRA_BASE_URL must be a valid absolute HTTPS URL.");
    }

    return new JiraSettings(baseUri, email, apiToken);
  }

  private static string GetRequired(string name, string repoRoot)
  {
    string? value = System.Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
      throw new InvalidOperationException(
        $"Missing required environment variable: {name}. Set it in the process environment or in '{Path.GetRelativePath(repoRoot, Path.Combine(repoRoot, ".env"))}'.");
    }

    return value.Trim();
  }
}
