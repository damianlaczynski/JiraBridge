using JiraBridge.Navigation.Commands;
using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Home;

public sealed class HomeViewModel(CommandPalette commandPalette)
{
  public IReadOnlyList<MenuItem> Items { get; } =
    commandPalette.Commands
      .Select(command => new MenuItem(command.Name, command.Description))
      .ToArray();
}
