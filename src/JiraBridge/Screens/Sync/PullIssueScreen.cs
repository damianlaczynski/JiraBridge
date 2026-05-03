using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Sync;

public sealed class PullIssueScreen() : MenuScreen("Pull issue")
{
  public override IReadOnlyList<string> GetLines() => ["Import or refresh one Jira issue into the repository."];
}
