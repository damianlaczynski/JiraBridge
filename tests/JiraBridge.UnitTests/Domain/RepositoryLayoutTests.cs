using JiraBridge.Domain.Configuration;
using Xunit;

namespace JiraBridge.UnitTests.Domain;

public sealed class RepositoryLayoutTests
{
  [Fact]
  public void Default_UsesPocCompatiblePaths()
  {
    var layout = RepositoryLayout.Default;

    Assert.Equal(".jirabridge/settings.json", layout.SettingsFile);
    Assert.Equal(".jirabridge/jira-project.json", layout.JiraMetadataFile);
    Assert.Equal("project-docs/backlog", layout.BacklogRoot);
  }
}
