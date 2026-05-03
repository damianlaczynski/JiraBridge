using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Sync;

public sealed class ConflictsScreen() : MenuScreen("Conflicts")
{
  public override IReadOnlyList<string> GetLines() => ["Review open sync conflicts and inspect resolution options."];
}
