namespace JiraBridge.Domain.Configuration;

public sealed record RepositoryLayout(
  string SettingsDirectory,
  string SettingsFile,
  string JiraMetadataFile,
  string ConflictsFile,
  string BacklogRoot)
{
  public static RepositoryLayout Default =>
    new(
      SettingsDirectory: ".jirabridge",
      SettingsFile: ".jirabridge/settings.json",
      JiraMetadataFile: ".jirabridge/jira-project.json",
      ConflictsFile: ".jirabridge/conflicts.json",
      BacklogRoot: "project-docs/backlog");
}
