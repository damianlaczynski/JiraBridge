namespace JiraBridge.Navigation.Commands;

public sealed class CommandPalette
{
  public IReadOnlyCollection<CommandDefinition> Commands { get; } =
  [
    new("configure", "Bootstrap repository settings and Jira metadata cache."),
    new("validate", "Validate local backlog artifacts."),
    new("push", "Push local changes to Jira."),
    new("pull", "Pull remote changes into repository."),
    new("conflicts", "Show open synchronization conflicts."),
    new("resolve", "Resolve one synchronization conflict.")
  ];
}
