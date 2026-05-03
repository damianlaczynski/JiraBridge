using JiraBridge.Application.Common;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Repository;
using JiraBridge.UnitTests.Support;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

[Collection("ProcessState")]
public sealed class BacklogValidatorTests
{
  [Fact]
  public async Task ValidateAsync_WhenJiraRefreshFails_ReturnsFailure()
  {
    string repoRoot = CreateRepositoryWithValidArtifact();
    using var scope = new TestProcessScope(
      repoRoot,
      "JIRABRIDGE_JIRA_BASE_URL",
      "JIRABRIDGE_JIRA_EMAIL",
      "JIRABRIDGE_JIRA_API_TOKEN");

    var validator = new BacklogValidator(new FakeRepositoryMetadataRefresher((_, _, _) =>
      throw new InvalidOperationException("jira unavailable")));

    CommandResult result = await validator.ValidateAsync(CancellationToken.None);

    Assert.False(result.Success);
    Assert.Contains("Could not load current project metadata", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
  }

  private static string CreateRepositoryWithValidArtifact()
  {
    string repoRoot = Path.Combine(Path.GetTempPath(), "jirabridge-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(repoRoot);
    Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
    Directory.CreateDirectory(Path.Combine(repoRoot, ".jirabridge"));
    Directory.CreateDirectory(Path.Combine(repoRoot, "docs", "jira-bridge", "backlog"));
    RepositorySettingsStore.Save(repoRoot, RepositorySettingsStore.CreateDefault("SCRUM"));

    var metadata = new RepositoryJiraConfiguration(
      "SCRUM",
      "100",
      "Scrum",
      "https://example.atlassian.net",
      [new JiraProjectIssueType("1", "Story", false)],
      [new JiraLinkType("10", "Blocks", "is blocked by", "blocks")],
      [
        new JiraIssueTypeStatuses(
          "1",
          "Story",
          [new JiraStatus("10000", "To Do", "To Do")])
      ]);

    RepositoryJiraConfigurationStore.Save(repoRoot, RepositorySettingsStore.CreateDefault("SCRUM"), metadata);

    File.WriteAllText(
      Path.Combine(repoRoot, "docs", "jira-bridge", "backlog", "sample.md"),
      """
      # Sample story

      ## Metadata

      - Issue Type: Story
      - Jira Issue Key:

      ## Links

      - Parent: none

      ## Relations

      ### Blocks

      - none

      ## Description

      Example description.
      """);

    return repoRoot;
  }
}
