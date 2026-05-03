using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Sync;

public sealed class PushScreen() : MenuScreen("Push")
{
  public override IReadOnlyList<string> GetLines() => ["Push local backlog changes to Jira."];
}
