using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Repository;
using Xunit;

namespace JiraBridge.UnitTests.Infrastructure;

public sealed class RepositorySettingsStoreTests
{
  [Fact]
  public void CreateDefault_UsesExpectedRepositoryDefaults()
  {
    RepositorySettings settings = RepositorySettingsStore.CreateDefault("SCRUM");

    Assert.Equal(1, settings.SchemaVersion);
    Assert.Equal("SCRUM", settings.JiraProjectKey);
    Assert.Equal("project-docs/backlog", settings.BacklogRoot);
    Assert.Equal(".jirabridge/jira-project.json", settings.MetadataFile);
    Assert.True(settings.SprintMappingEnabled);
  }
}
