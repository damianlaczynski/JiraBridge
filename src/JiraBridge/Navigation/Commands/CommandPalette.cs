namespace JiraBridge.Navigation.Commands;

public sealed class CommandPalette
{
  public IReadOnlyCollection<CommandDefinition> Commands { get; } =
  [
    new("configure", "Bootstrap repository settings and Jira metadata cache."),
    new("validate", "Validate local backlog artifacts."),
    new("push", "Push local changes to Jira."),
    new("push-issue", "Push exactly one linked artifact (by issue key) to Jira."),
    new("pull", "Pull remote changes into repository."),
    new("pull-issue", "Pull exactly one Jira issue into the repository."),
    new("conflicts", "Show open synchronization conflicts."),
    new("resolve", "Resolve one synchronization conflict.")
  ];
}
