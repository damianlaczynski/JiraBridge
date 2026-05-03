namespace JiraBridge.Navigation.Commands;

public sealed class CommandSuggestionEngine
{
  public IReadOnlyList<CommandDefinition> Suggest(string? input, IReadOnlyCollection<CommandDefinition> commands)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      return commands.OrderBy(command => command.Name).ToArray();
    }

    return commands
      .Where(command => command.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
      .OrderBy(command => command.Name)
      .ToArray();
  }
}
