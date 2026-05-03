using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Configuration;

public sealed class ConfigurationScreen(ConfigurationViewModel viewModel) : MenuScreen("Configuration")
{
  public override IReadOnlyList<string> GetLines() => [viewModel.Description];
}
