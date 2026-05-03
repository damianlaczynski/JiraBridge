using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Home;

public sealed class HomeScreen(HomeViewModel viewModel) : MenuScreen("JiraBridge")
{
  public override IReadOnlyList<string> GetLines() =>
    [
      "Interactive CLI home screen",
      string.Empty,
      ..viewModel.Items.Select(item => $"{item.Label} - {item.Description}")
    ];
}
