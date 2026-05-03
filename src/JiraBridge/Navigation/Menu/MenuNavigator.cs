namespace JiraBridge.Navigation.Menu;

public sealed class MenuNavigator
{
  public IMenuScreen Current { get; private set; } = null!;

  public void SetCurrent(IMenuScreen screen)
  {
    Current = screen;
  }
}
