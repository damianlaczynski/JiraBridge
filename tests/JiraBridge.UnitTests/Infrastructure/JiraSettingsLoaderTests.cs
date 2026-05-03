using JiraBridge.Infrastructure.Environment;
using JiraBridge.UnitTests.Support;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

[Collection("ProcessState")]
public sealed class JiraSettingsLoaderTests
{
  [Fact]
  public void LoadFromEnvironment_ReadsValuesFromDotEnvWhenProcessVariablesAreMissing()
  {
    string repoRoot = Path.Combine(Path.GetTempPath(), "jirabridge-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(repoRoot);
    File.WriteAllText(
      Path.Combine(repoRoot, ".env"),
      """
      JIRABRIDGE_JIRA_BASE_URL=https://example.atlassian.net
      JIRABRIDGE_JIRA_EMAIL=test@example.com
      JIRABRIDGE_JIRA_API_TOKEN=secret-token
      """);

    using var scope = new TestProcessScope(
      repoRoot,
      "JIRABRIDGE_JIRA_BASE_URL",
      "JIRABRIDGE_JIRA_EMAIL",
      "JIRABRIDGE_JIRA_API_TOKEN");

    scope.SetEnvironmentVariable("JIRABRIDGE_JIRA_BASE_URL", null);
    scope.SetEnvironmentVariable("JIRABRIDGE_JIRA_EMAIL", null);
    scope.SetEnvironmentVariable("JIRABRIDGE_JIRA_API_TOKEN", null);

    JiraSettings settings = JiraSettingsLoader.LoadFromEnvironment(repoRoot);

    Assert.Equal("https://example.atlassian.net/", settings.BaseUri.ToString());
    Assert.Equal("test@example.com", settings.Email);
    Assert.Equal("secret-token", settings.ApiToken);
  }
}
