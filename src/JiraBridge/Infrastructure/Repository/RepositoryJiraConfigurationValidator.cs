using JiraBridge.Domain.Configuration;

namespace JiraBridge.Infrastructure.Repository;

public static class RepositoryJiraConfigurationValidator
{
  public static IReadOnlyList<ValidationIssue> Validate(
    RepositoryJiraConfiguration configuration,
    string repoRoot,
    RepositorySettings settings)
  {
    string configPath = RepositoryJiraConfigurationStore.GetPath(repoRoot, settings);
    var issues = new List<ValidationIssue>();

    if (string.IsNullOrWhiteSpace(configuration.ProjectKey))
    {
      issues.Add(new ValidationIssue(configPath, "Jira project configuration is missing 'projectKey'."));
    }

    if (string.IsNullOrWhiteSpace(configuration.ProjectId))
    {
      issues.Add(new ValidationIssue(configPath, "Jira metadata cache is missing 'projectId'. Re-run configure from the JiraBridge home screen."));
    }

    if (configuration.IssueTypes.Count == 0)
    {
      issues.Add(new ValidationIssue(configPath, "Jira metadata cache does not contain any issue types. Re-run configure from the JiraBridge home screen."));
    }

    if (configuration.LinkTypes.Count == 0)
    {
      issues.Add(new ValidationIssue(configPath, "Jira metadata cache does not contain any link types. Re-run configure from the JiraBridge home screen."));
    }

    if (configuration.IssueTypeStatuses.Count == 0)
    {
      issues.Add(new ValidationIssue(configPath, "Jira metadata cache does not contain any issue type statuses. Re-run configure from the JiraBridge home screen."));
    }

    foreach (JiraProjectIssueType issueType in configuration.IssueTypes)
    {
      bool hasStatuses = configuration.IssueTypeStatuses.Any(statuses =>
        string.Equals(statuses.IssueTypeId, issueType.Id, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(statuses.IssueTypeName, issueType.Name, StringComparison.OrdinalIgnoreCase));

      if (!hasStatuses)
      {
        issues.Add(new ValidationIssue(
          configPath,
          $"Jira issue type '{issueType.Name}' does not have associated statuses in the local metadata cache. Re-run configure from the JiraBridge home screen."));
      }
    }

    return issues;
  }
}
