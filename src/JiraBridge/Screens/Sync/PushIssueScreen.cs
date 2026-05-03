using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Sync;

public sealed class PushIssueScreen() : MenuScreen("Push issue")
{
  public override IReadOnlyList<string> GetLines() => ["Push a single artifact that already has a Jira Issue Key."];
}
