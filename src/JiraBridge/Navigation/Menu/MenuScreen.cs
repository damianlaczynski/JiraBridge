namespace JiraBridge.Navigation.Menu;

public abstract class MenuScreen(string title) : IMenuScreen
{
  public string Title { get; } = title;

  public abstract IReadOnlyList<string> GetLines();
}
