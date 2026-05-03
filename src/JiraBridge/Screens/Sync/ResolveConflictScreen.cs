using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Sync;

public sealed class ResolveConflictScreen() : MenuScreen("Resolve Conflict")
{
  public override IReadOnlyList<string> GetLines() => ["Resolve a single conflict using repo, jira, or merge strategy."];
}
