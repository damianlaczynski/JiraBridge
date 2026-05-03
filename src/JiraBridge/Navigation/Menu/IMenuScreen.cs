namespace JiraBridge.Navigation.Menu;

public interface IMenuScreen
{
  string Title { get; }

  IReadOnlyList<string> GetLines();
}
