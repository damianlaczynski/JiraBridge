using JiraBridge.Domain.Configuration;

namespace JiraBridge.Infrastructure.Repository;

public static class RepositoryJiraIssueTypeQueries
{
  public static bool IsSubtask(RepositoryJiraConfiguration configuration, string issueTypeName)
  {
    return configuration.IssueTypes.Exists(issueType =>
      string.Equals(issueType.Name, issueTypeName, StringComparison.OrdinalIgnoreCase) && issueType.Subtask);
  }
}
