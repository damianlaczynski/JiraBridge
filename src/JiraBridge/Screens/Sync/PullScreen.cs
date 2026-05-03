using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Sync;

public sealed class PullScreen() : MenuScreen("Pull")
{
  public override IReadOnlyList<string> GetLines() => ["Import Jira changes into repository artifacts."];
}
