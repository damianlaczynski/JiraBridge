using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;
using JiraBridge.Application.Configuration;
using JiraBridge.Application.Sync;
using JiraBridge.Application.Validation;
using JiraBridge.Domain.Sync;
using JiraBridge.Host.Terminal;
using JiraBridge.Navigation.Commands;
using JiraBridge.Navigation.Menu;
using JiraBridge.Screens.Configuration;
using JiraBridge.Screens.Home;
using JiraBridge.Screens.Sync;
using JiraBridge.Screens.Validation;
using Xunit;

namespace JiraBridge.UnitTests.Host;

public sealed class InteractiveShellControllerTests
{
  [Fact]
  public async Task HandleKeyAsync_FiltersCommandsOnHomeScreen()
  {
    InteractiveShellController controller = CreateController();
    controller.Initialize();

    await controller.HandleKeyAsync(new ConsoleKeyInfo('p', ConsoleKey.P, false, false, false), CancellationToken.None);
    await controller.HandleKeyAsync(new ConsoleKeyInfo('u', ConsoleKey.U, false, false, false), CancellationToken.None);

    IReadOnlyList<string> lines = controller.GetLines();

    Assert.Contains(lines, line => line.Contains("push - Push local changes to Jira.", StringComparison.Ordinal));
    Assert.DoesNotContain(lines, line => line.Contains("validate -", StringComparison.Ordinal));
  }

  [Fact]
  public async Task HandleKeyAsync_ResolvesConflictThroughInteractiveFlow()
  {
    var conflictStore = new FakeConflictStore(
      [
        new SyncConflict("SCRUM-2", "project-docs/backlog/story.md", "Local story", "Story", "push", "summary", "Summary:\n- repo\n+ jira", DateTimeOffset.UtcNow)
      ]);

    var resolver = new FakeConflictResolver(conflictStore);
    InteractiveShellController controller = CreateController(conflictStore, resolver);
    controller.Initialize();

    await controller.HandleKeyAsync(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false), CancellationToken.None);
    await controller.HandleKeyAsync(new ConsoleKeyInfo('o', ConsoleKey.O, false, false, false), CancellationToken.None);
    await controller.HandleKeyAsync(new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false), CancellationToken.None);
    await controller.HandleKeyAsync(new ConsoleKeyInfo('f', ConsoleKey.F, false, false, false), CancellationToken.None);
    await controller.HandleKeyAsync(new ConsoleKeyInfo('l', ConsoleKey.L, false, false, false), CancellationToken.None);
    await controller.HandleKeyAsync(new ConsoleKeyInfo('i', ConsoleKey.I, false, false, false), CancellationToken.None);
    await controller.HandleKeyAsync(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), CancellationToken.None);

    Assert.Equal("Conflicts", controller.Title);
    Assert.Contains(controller.GetLines(), line => line.Contains("SCRUM-2", StringComparison.Ordinal));

    await controller.HandleKeyAsync(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), CancellationToken.None);
    Assert.Equal("Resolve Conflict", controller.Title);

    await controller.HandleKeyAsync(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), CancellationToken.None);

    Assert.Equal("Conflicts", controller.Title);
    Assert.Contains(controller.GetLines(), line => line.Contains("No open conflicts.", StringComparison.Ordinal));
    Assert.Equal(ConflictResolutionStrategy.Repository, resolver.LastStrategy);
    Assert.Equal("SCRUM-2", resolver.LastIssueKey);
  }

  private static InteractiveShellController CreateController(
    IConflictStore? conflictStore = null,
    IConflictResolver? resolver = null)
  {
    conflictStore ??= new FakeConflictStore([]);
    resolver ??= new FakeConflictResolver((FakeConflictStore)conflictStore);

    var commandPalette = new CommandPalette();
    var suggestionEngine = new CommandSuggestionEngine();
    var homeScreen = new HomeScreen(new HomeViewModel(commandPalette));
    var configurationScreen = new ConfigurationScreen(new ConfigurationViewModel());
    var validationScreen = new ValidationScreen(new ValidationViewModel());
    var pushScreen = new PushScreen();
    var pullScreen = new PullScreen();
    var conflictsScreen = new ConflictsScreen();
    var resolveConflictScreen = new ResolveConflictScreen();

    return new InteractiveShellController(
      new MenuNavigator(),
      commandPalette,
      suggestionEngine,
      homeScreen,
      configurationScreen,
      validationScreen,
      pushScreen,
      pullScreen,
      conflictsScreen,
      resolveConflictScreen,
      new ScreenRenderer(),
      new OperationProgressTracker(),
      new ConfigureRepositoryCommandHandler(new FakeRepositoryBootstrapper()),
      new ValidateRepositoryCommandHandler(new FakeBacklogValidator()),
      new PushChangesCommandHandler(new FakeSyncExecutor()),
      new PullChangesCommandHandler(new FakeSyncExecutor()),
      new GetConflictsQueryHandler(conflictStore),
      new ResolveConflictCommandHandler(resolver));
  }

  private sealed class FakeRepositoryBootstrapper : IRepositoryBootstrapper
  {
    public Task<CommandResult> ConfigureAsync(string projectKey, CancellationToken cancellationToken) =>
      Task.FromResult(CommandResult.Ok($"Configured {projectKey}."));
  }

  private sealed class FakeBacklogValidator : IBacklogValidator
  {
    public Task<CommandResult> ValidateAsync(CancellationToken cancellationToken) =>
      Task.FromResult(CommandResult.Ok("Validation passed."));
  }

  private sealed class FakeSyncExecutor : ISyncExecutor
  {
    public Task<CommandResult> PullAsync(CancellationToken cancellationToken) =>
      Task.FromResult(CommandResult.Ok("Pull executed."));

    public Task<CommandResult> PushAsync(bool dryRun, CancellationToken cancellationToken) =>
      Task.FromResult(CommandResult.Ok("Push executed."));
  }

  private sealed class FakeConflictStore(IReadOnlyCollection<SyncConflict> initialItems) : IConflictStore
  {
    private readonly List<SyncConflict> items = [.. initialItems];

    public Task<IReadOnlyCollection<SyncConflict>> GetOpenConflictsAsync(CancellationToken cancellationToken) =>
      Task.FromResult((IReadOnlyCollection<SyncConflict>)items.ToArray());

    public void Remove(string issueKey) =>
      items.RemoveAll(item => string.Equals(item.IssueKey, issueKey, StringComparison.OrdinalIgnoreCase));
  }

  private sealed class FakeConflictResolver(FakeConflictStore store) : IConflictResolver
  {
    public string? LastIssueKey { get; private set; }

    public ConflictResolutionStrategy? LastStrategy { get; private set; }

    public async Task<CommandResult> ResolveAsync(string issueKey, ConflictResolutionStrategy strategy, CancellationToken cancellationToken)
    {
      LastIssueKey = issueKey;
      LastStrategy = strategy;
      store.Remove(issueKey);
      return await Task.FromResult(CommandResult.Ok($"Resolved {issueKey}."));
    }
  }
}
