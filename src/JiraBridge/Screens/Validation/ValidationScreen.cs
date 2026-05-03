using JiraBridge.Navigation.Menu;

namespace JiraBridge.Screens.Validation;

public sealed class ValidationScreen(ValidationViewModel viewModel) : MenuScreen("Validation")
{
  public override IReadOnlyList<string> GetLines() => [viewModel.Description];
}
