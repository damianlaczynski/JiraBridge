using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Storage;
using System.Text;

namespace JiraBridge.Infrastructure.Repository;

public sealed class RepositoryBootstrapper(
  IRepositoryMetadataRefresher metadataRefresher,
  IOperationProgressSink progressSink) : IRepositoryBootstrapper
{
  public async Task<CommandResult> ConfigureAsync(string projectKey, CancellationToken cancellationToken)
  {
    progressSink.Start("Configure", $"Connecting to Jira project '{projectKey}'...", totalSteps: 7);

    string repoRoot = RepositoryRootResolver.Resolve(null);
    RepositorySettings settings = RepositorySettingsStore.TryLoad(repoRoot, out _)
      ?? RepositorySettingsStore.CreateDefault(projectKey);

    settings = settings with
    {
      JiraProjectKey = projectKey
    };

    RepositorySettingsStore.Save(repoRoot, settings);
    progressSink.ReportStep("Saved repository settings.");

    Directory.CreateDirectory(PathResolver.ResolveRepoRelativePath(repoRoot, settings.BacklogRoot));
    EnsureEnvExample(repoRoot);
    progressSink.ReportStep("Prepared backlog workspace and local templates.");

    RepositoryJiraConfiguration configuration = await metadataRefresher.RefreshAsync(repoRoot, settings, cancellationToken);
    progressSink.ReportStep($"Cached Jira metadata for project '{configuration.ProjectKey}'.");

    string settingsPath = Path.GetRelativePath(repoRoot, RepositorySettingsStore.GetPath(repoRoot));
    string metadataPath = Path.GetRelativePath(repoRoot, RepositoryJiraConfigurationStore.GetPath(repoRoot, settings));
    progressSink.ReportStep("Repository configure flow completed.");
    progressSink.Complete($"Configure completed for '{projectKey}'.");

    return CommandResult.Ok(
      $"Repository configuration saved for Jira project '{projectKey}'.",
      "[OK] Settings saved and workspace prepared.",
      $"[INFO] Settings file: {settingsPath}",
      $"[INFO] Backlog root: {settings.BacklogRoot}",
      $"[INFO] Metadata cache: {metadataPath}",
      $"[OK] Jira project detected: {configuration.ProjectKey} ({configuration.ProjectName})",
      $"[INFO] Issue types: {configuration.IssueTypes.Count}",
      $"[INFO] Link types: {configuration.LinkTypes.Count}",
      $"[INFO] Issue type status sets: {configuration.IssueTypeStatuses.Count}");
  }

  private static void EnsureEnvExample(string repoRoot)
  {
    string path = Path.Combine(repoRoot, ".env.example");
    if (File.Exists(path))
    {
      return;
    }

    File.WriteAllText(
      path,
      "# Create API token: https://id.atlassian.com/manage-profile/security/api-tokens" + System.Environment.NewLine +
      "# Docs: https://github.com/DamianLaczynski/JiraBridge/blob/main/docs/user-guide.md#how-to-get-a-jira-api-token" + System.Environment.NewLine +
      "JIRABRIDGE_JIRA_BASE_URL=https://your-company.atlassian.net" + System.Environment.NewLine +
      "JIRABRIDGE_JIRA_EMAIL=your.email@company.com" + System.Environment.NewLine +
      "JIRABRIDGE_JIRA_API_TOKEN=your_api_token" + System.Environment.NewLine,
      Encoding.UTF8);
  }
}
