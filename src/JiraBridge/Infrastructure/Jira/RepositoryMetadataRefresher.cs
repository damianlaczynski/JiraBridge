using JiraBridge.Application.Abstractions;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Environment;
using JiraBridge.Infrastructure.Repository;

namespace JiraBridge.Infrastructure.Jira;

public sealed class RepositoryMetadataRefresher(
  IJiraApiClientFactory jiraApiClientFactory,
  IOperationProgressSink progressSink) : IRepositoryMetadataRefresher
{
  public static bool ShouldRefreshSprintProjection(
    RepositorySettings repositorySettings,
    RepositoryJiraConfiguration configuration) =>
    repositorySettings.SprintMappingEnabled && !configuration.SprintProjectionCached;

  public async Task<RepositoryJiraConfiguration> RefreshAsync(
    string repoRoot,
    RepositorySettings repositorySettings,
    CancellationToken cancellationToken)
  {
    JiraSettings settings = JiraSettingsLoader.LoadFromEnvironment(repoRoot);
    progressSink.ReportInfo($"Using Jira base URL {settings.BaseUri}.");

    using var client = jiraApiClientFactory.Create(settings);
    JiraProjectInfo projectInfo = await client.GetProjectInfoAsync(repositorySettings.JiraProjectKey, cancellationToken);
    progressSink.ReportStep("Authenticated against Jira and loaded project metadata.");
    IReadOnlyList<JiraProjectIssueType> issueTypes = await client.GetProjectIssueTypesAsync(repositorySettings.JiraProjectKey, cancellationToken);
    progressSink.ReportStep("Loaded issue types.");
    IReadOnlyList<JiraLinkType> linkTypes = await client.GetLinkTypesAsync(cancellationToken);
    IReadOnlyList<JiraIssueTypeStatuses> issueTypeStatuses = await client.GetProjectIssueTypeStatusesAsync(repositorySettings.JiraProjectKey, cancellationToken);
    string? sprintFieldId = null;
    List<JiraSprintInfo> sprints = [];
    if (repositorySettings.SprintMappingEnabled)
    {
      sprintFieldId = await client.GetSprintFieldIdAsync(cancellationToken);
      sprints = (await client.GetProjectSprintsAsync(repositorySettings.JiraProjectKey, cancellationToken)).ToList();
    }

    progressSink.ReportStep(repositorySettings.SprintMappingEnabled
      ? "Loaded link types, workflow statuses, and sprint metadata."
      : "Loaded link types and workflow statuses.");

    var configuration = new RepositoryJiraConfiguration(
      ProjectKey: projectInfo.Key,
      ProjectId: projectInfo.Id,
      ProjectName: projectInfo.Name,
      BaseUrl: settings.BaseUri.ToString(),
      IssueTypes: issueTypes.ToList(),
      LinkTypes: linkTypes.ToList(),
      IssueTypeStatuses: issueTypeStatuses.ToList(),
      SprintFieldId: sprintFieldId,
      Sprints: sprints,
      SprintProjectionCached: repositorySettings.SprintMappingEnabled);

    RepositoryJiraConfigurationStore.Save(repoRoot, repositorySettings, configuration);
    return configuration;
  }
}
