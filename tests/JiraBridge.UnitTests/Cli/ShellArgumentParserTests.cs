using JiraBridge.Navigation.Commands;
using JiraBridge.Domain.Sync;
using Xunit;

namespace JiraBridge.UnitTests.Cli;

public sealed class CommandSuggestionEngineTests
{
  [Fact]
  public void Suggest_WithoutInput_ReturnsSortedCommands()
  {
    var engine = new CommandSuggestionEngine();
    var commands = new[]
    {
      new CommandDefinition("push", "Push"),
      new CommandDefinition("configure", "Configure")
    };

    var results = engine.Suggest(null, commands);

    Assert.Collection(
      results,
      command => Assert.Equal("configure", command.Name),
      command => Assert.Equal("push", command.Name));
  }

  [Fact]
  public void Suggest_FiltersByPartialName()
  {
    var engine = new CommandSuggestionEngine();
    var commands = new[]
    {
      new CommandDefinition("push", "Push"),
      new CommandDefinition("pull", "Pull"),
      new CommandDefinition("configure", "Configure")
    };

    var results = engine.Suggest("pu", commands);

    Assert.Collection(
      results,
      command => Assert.Equal("pull", command.Name),
      command => Assert.Equal("push", command.Name));
  }
}
