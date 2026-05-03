using JiraBridge.Application.Common;
using JiraBridge.Application.Configuration;
using JiraBridge.Application.Sync;
using JiraBridge.Application.Validation;
using JiraBridge.Application.Abstractions;
using JiraBridge.Domain.Sync;
using JiraBridge.Navigation.Commands;
using JiraBridge.Navigation.Menu;
using JiraBridge.Screens.Configuration;
using JiraBridge.Screens.Home;
using JiraBridge.Screens.Sync;
using JiraBridge.Screens.Validation;

namespace JiraBridge.Host.Terminal;

public sealed class InteractiveShellController(
  MenuNavigator menuNavigator,
  CommandPalette commandPalette,
  CommandSuggestionEngine suggestionEngine,
  HomeScreen homeScreen,
  ConfigurationScreen configurationScreen,
  ValidationScreen validationScreen,
  PushScreen pushScreen,
  PullScreen pullScreen,
  ConflictsScreen conflictsScreen,
  ResolveConflictScreen resolveConflictScreen,
  ScreenRenderer screenRenderer,
  IOperationProgressSink progressSink,
  ConfigureRepositoryCommandHandler configureHandler,
  ValidateRepositoryCommandHandler validateHandler,
  PushChangesCommandHandler pushHandler,
  PullChangesCommandHandler pullHandler,
  GetConflictsQueryHandler conflictsHandler,
  ResolveConflictCommandHandler resolveHandler)
{
  private readonly List<SyncConflict> conflicts = [];
  private readonly List<ConflictResolutionStrategy> resolutionStrategies =
  [
    ConflictResolutionStrategy.Repository,
    ConflictResolutionStrategy.Jira,
    ConflictResolutionStrategy.Merge
  ];

  private string commandFilter = string.Empty;
  private string configurationProjectKey = string.Empty;
  private bool isEditingHomeFilter;
  private int selectedHomeIndex;
  private int selectedConflictIndex;
  private int selectedStrategyIndex;
  private bool pushDryRunMode;
  private string progressIndicator = "[..]";
  private string screenMessage = string.Empty;
  private string[] screenDetails = [];
  private static readonly string[] SpinnerFrames = ["[|]", "[/]", "[-]", "[\\]"];

  public void Initialize()
  {
    menuNavigator.SetCurrent(homeScreen);
    ResetHomeState();
  }

  public string Title => menuNavigator.Current.Title;

  public CursorPlacement GetCursorPlacement()
  {
    return menuNavigator.Current switch
    {
      HomeScreen when isEditingHomeFilter => BuildInputCursorPlacement("Filter: ", commandFilter, lineIndex: 6),
      ConfigurationScreen => BuildInputCursorPlacement("Project key: ", configurationProjectKey, lineIndex: 4),
      _ => new CursorPlacement(IsVisible: false, Left: 0, Top: 0)
    };
  }

  public IReadOnlyList<string> GetLines()
  {
    return menuNavigator.Current switch
    {
      HomeScreen => BuildHomeLines(),
      ConfigurationScreen => BuildConfigurationLines(),
      ValidationScreen => BuildResultLines("Run validation against local artifacts and Jira metadata."),
      PushScreen => BuildPushLines(),
      PullScreen => BuildResultLines("Import Jira changes into repository artifacts."),
      ConflictsScreen => BuildConflictsLines(),
      ResolveConflictScreen => BuildResolveLines(),
      _ => ["Unknown screen."]
    };
  }

  public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
  {
    if (menuNavigator.Current is HomeScreen)
    {
      return await HandleHomeKeyAsync(key, cancellationToken);
    }

    if (menuNavigator.Current is ConfigurationScreen)
    {
      return await HandleConfigurationKeyAsync(key, cancellationToken);
    }

    if (menuNavigator.Current is ConflictsScreen)
    {
      return await HandleConflictsKeyAsync(key, cancellationToken);
    }

    if (menuNavigator.Current is PushScreen)
    {
      return await HandlePushKeyAsync(cancellationToken, key);
    }

    if (menuNavigator.Current is ResolveConflictScreen)
    {
      return await HandleResolveKeyAsync(key, cancellationToken);
    }

    return HandleResultKey(key);
  }

  private async Task<bool> HandlePushKeyAsync(CancellationToken cancellationToken, ConsoleKeyInfo key)
  {
    switch (key.Key)
    {
      case ConsoleKey.Escape:
        ReturnHome();
        return true;
      case ConsoleKey.D:
        pushDryRunMode = true;
        await ExecuteCommandResultAsync(
          pushScreen,
          () => pushHandler.HandleAsync(new PushChangesCommand(DryRun: true), cancellationToken),
          fallbackOperationName: "Push Dry-Run");
        return true;
      case ConsoleKey.R:
        await ExecuteCommandResultAsync(
          pushScreen,
          () => pushHandler.HandleAsync(new PushChangesCommand(DryRun: pushDryRunMode), cancellationToken),
          fallbackOperationName: pushDryRunMode ? "Push Dry-Run" : "Push");
        return true;
      case ConsoleKey.Enter:
        pushDryRunMode = false;
        await ExecuteCommandResultAsync(
          pushScreen,
          () => pushHandler.HandleAsync(new PushChangesCommand(DryRun: false), cancellationToken),
          fallbackOperationName: "Push");
        return true;
      default:
        return true;
    }
  }

  private async Task<bool> HandleHomeKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
  {
    switch (key.Key)
    {
      case ConsoleKey.UpArrow:
        isEditingHomeFilter = false;
        MoveHomeSelection(-1);
        return true;
      case ConsoleKey.DownArrow:
        isEditingHomeFilter = false;
        MoveHomeSelection(1);
        return true;
      case ConsoleKey.Backspace:
        if (commandFilter.Length > 0)
        {
          isEditingHomeFilter = true;
          commandFilter = commandFilter[..^1];
          ClampHomeSelection();
        }

        return true;
      case ConsoleKey.Escape:
        if (commandFilter.Length > 0)
        {
          isEditingHomeFilter = false;
          commandFilter = string.Empty;
          ClampHomeSelection();
        }

        return true;
      case ConsoleKey.Enter:
        isEditingHomeFilter = false;
        return await ExecuteSelectedCommandAsync(cancellationToken);
      case ConsoleKey.Q:
        if (string.IsNullOrWhiteSpace(commandFilter))
        {
          isEditingHomeFilter = false;
          return false;
        }

        goto default;
      default:
        if (!char.IsControl(key.KeyChar))
        {
          isEditingHomeFilter = true;
          commandFilter += key.KeyChar;
          ClampHomeSelection();
        }

        return true;
    }
  }

  private async Task<bool> HandleConfigurationKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
  {
    switch (key.Key)
    {
      case ConsoleKey.Escape:
        ReturnHome();
        return true;
      case ConsoleKey.Backspace:
        if (configurationProjectKey.Length > 0)
        {
          configurationProjectKey = configurationProjectKey[..^1];
        }

        return true;
      case ConsoleKey.Enter:
        if (string.IsNullOrWhiteSpace(configurationProjectKey))
        {
          screenMessage = "Provide a Jira project key before running configure.";
          screenDetails = [];
          return true;
        }

        await ExecuteCommandResultAsync(
          configurationScreen,
          () => configureHandler.HandleAsync(new ConfigureRepositoryCommand(configurationProjectKey.Trim()), cancellationToken));
        return true;
      default:
        if (!char.IsControl(key.KeyChar))
        {
          configurationProjectKey += key.KeyChar;
        }

        return true;
    }
  }

  private async Task<bool> HandleConflictsKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
  {
    switch (key.Key)
    {
      case ConsoleKey.Escape:
        ReturnHome();
        return true;
      case ConsoleKey.R:
        await LoadConflictsAsync(cancellationToken);
        return true;
      case ConsoleKey.UpArrow:
        MoveConflictSelection(-1);
        return true;
      case ConsoleKey.DownArrow:
        MoveConflictSelection(1);
        return true;
      case ConsoleKey.Enter:
        if (conflicts.Count == 0)
        {
          return true;
        }

        selectedStrategyIndex = 0;
        menuNavigator.SetCurrent(resolveConflictScreen);
        screenMessage = string.Empty;
        screenDetails = [];
        return true;
      default:
        return true;
    }
  }

  private async Task<bool> HandleResolveKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
  {
    switch (key.Key)
    {
      case ConsoleKey.Escape:
        menuNavigator.SetCurrent(conflictsScreen);
        return true;
      case ConsoleKey.UpArrow:
        MoveStrategySelection(-1);
        return true;
      case ConsoleKey.DownArrow:
        MoveStrategySelection(1);
        return true;
      case ConsoleKey.Enter:
        if (conflicts.Count == 0)
        {
          menuNavigator.SetCurrent(conflictsScreen);
          return true;
        }

        SyncConflict conflict = conflicts[selectedConflictIndex];
        ConflictResolutionStrategy strategy = resolutionStrategies[selectedStrategyIndex];
        await ExecuteCommandResultAsync(
          resolveConflictScreen,
          () => resolveHandler.HandleAsync(new ResolveConflictCommand(conflict.IssueKey, strategy), cancellationToken));
        await LoadConflictsAsync(cancellationToken);
        if (conflicts.Count == 0)
        {
          menuNavigator.SetCurrent(conflictsScreen);
        }
        else
        {
          selectedConflictIndex = Math.Clamp(selectedConflictIndex, 0, conflicts.Count - 1);
        }

        return true;
      default:
        return true;
    }
  }

  private bool HandleResultKey(ConsoleKeyInfo key)
  {
    switch (key.Key)
    {
      case ConsoleKey.Escape:
      case ConsoleKey.Enter:
        ReturnHome();
        return true;
      default:
        return true;
    }
  }

  private async Task<bool> ExecuteSelectedCommandAsync(CancellationToken cancellationToken)
  {
    IReadOnlyList<CommandDefinition> visibleCommands = GetVisibleCommands();
    if (visibleCommands.Count == 0)
    {
      screenMessage = "No command matches the current filter.";
      screenDetails = [];
      return true;
    }

    string commandName = visibleCommands[selectedHomeIndex].Name;
    switch (commandName)
    {
      case "configure":
        menuNavigator.SetCurrent(configurationScreen);
        screenMessage = "Provide a Jira project key, then press Enter to refresh local Jira metadata and repository settings.";
        screenDetails = [];
        return true;
      case "validate":
        await ExecuteCommandResultAsync(
          validationScreen,
          () => validateHandler.HandleAsync(new ValidateRepositoryCommand(), cancellationToken));
        return true;
      case "push":
        menuNavigator.SetCurrent(pushScreen);
        screenMessage = "Choose Enter for push or D for dry-run preview.";
        screenDetails = [];
        return true;
      case "pull":
        await ExecuteCommandResultAsync(
          pullScreen,
          () => pullHandler.HandleAsync(new PullChangesCommand(), cancellationToken));
        return true;
      case "conflicts":
        await LoadConflictsAsync(cancellationToken);
        menuNavigator.SetCurrent(conflictsScreen);
        return true;
      case "resolve":
        await LoadConflictsAsync(cancellationToken);
        menuNavigator.SetCurrent(conflictsScreen);
        if (conflicts.Count > 0)
        {
          menuNavigator.SetCurrent(resolveConflictScreen);
        }

        return true;
      default:
        return true;
    }
  }

  private async Task ExecuteCommandResultAsync(
    IMenuScreen targetScreen,
    Func<Task<CommandResult>> operation,
    string? fallbackOperationName = null)
  {
    menuNavigator.SetCurrent(targetScreen);
    progressSink.Reset();
    progressSink.Start(fallbackOperationName ?? targetScreen.Title, "Working...");

    try
    {
      Task<CommandResult> operationTask = operation();
      int frameIndex = 0;

      while (!operationTask.IsCompleted)
      {
        progressIndicator = SpinnerFrames[frameIndex % SpinnerFrames.Length];
        frameIndex++;
        screenRenderer.Render(Title, GetLines(), GetCursorPlacement());
        await Task.WhenAny(operationTask, Task.Delay(90, CancellationToken.None));
      }

      progressIndicator = "[OK]";
      CommandResult result = await operationTask;
      screenMessage = result.Message;
      screenDetails = result.Details?.ToArray() ?? [];
    }
    catch (Exception exception)
    {
      progressIndicator = "[ERR]";
      progressSink.Fail(exception.Message);
      screenMessage = exception.Message;
      screenDetails = [];
    }
  }

  private async Task LoadConflictsAsync(CancellationToken cancellationToken)
  {
    try
    {
      IReadOnlyCollection<SyncConflict> items = await conflictsHandler.HandleAsync(new GetConflictsQuery(), cancellationToken);
      conflicts.Clear();
      conflicts.AddRange(items.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase));
      selectedConflictIndex = conflicts.Count == 0 ? 0 : Math.Clamp(selectedConflictIndex, 0, conflicts.Count - 1);
      screenMessage = conflicts.Count == 0 ? "No open conflicts." : $"Open conflicts: {conflicts.Count}.";
      screenDetails = [];
    }
    catch (Exception exception)
    {
      conflicts.Clear();
      selectedConflictIndex = 0;
      screenMessage = exception.Message;
      screenDetails = [];
    }
  }

  private IReadOnlyList<string> BuildHomeLines()
  {
    IReadOnlyList<CommandDefinition> commands = GetVisibleCommands();
    var lines = new List<string>
    {
      "Arrow keys: navigate",
      "Type: filter commands",
      "Enter: open or run",
      "Esc: clear filter",
      "Q: quit",
      string.Empty,
      $"Filter: {commandFilter}",
      string.Empty
    };

    if (commands.Count == 0)
    {
      lines.Add("No matching commands.");
      return lines;
    }

    for (int i = 0; i < commands.Count; i++)
    {
      CommandDefinition command = commands[i];
      string marker = i == selectedHomeIndex ? ">" : " ";
      lines.Add($"{marker} {command.Name} - {command.Description}");
    }

    return lines;
  }

  private IReadOnlyList<string> BuildConfigurationLines()
  {
    var lines = new List<string>
    {
      "[TIP] Enter a Jira project key such as SCRUM or OPS.",
      "[TIP] Configure will save settings, prepare backlog folders, and refresh Jira metadata.",
      "[TIP] Esc returns to the home screen.",
      string.Empty,
      $"Project key: {configurationProjectKey}",
      string.Empty
    };

    AppendProgress(lines);
    AppendOutcome(lines);
    return lines;
  }

  private IReadOnlyList<string> BuildResultLines(string description)
  {
    var lines = new List<string>
    {
      description,
      "Enter or Esc returns to the home screen.",
      string.Empty
    };

    AppendProgress(lines);
    AppendOutcome(lines);
    return lines;
  }

  private IReadOnlyList<string> BuildPushLines()
  {
    var lines = new List<string>
    {
      "[TIP] Enter: run push",
      "[TIP] D: dry-run preview",
      "[TIP] R: repeat the last push mode",
      "[TIP] Esc: home",
      string.Empty,
      $"Selected mode: {(pushDryRunMode ? "dry-run preview" : "apply Jira updates")}",
      string.Empty
    };

    AppendProgress(lines);
    AppendOutcome(lines);
    return lines;
  }

  private IReadOnlyList<string> BuildConflictsLines()
  {
    var lines = new List<string>
    {
      "Arrow keys: navigate conflicts",
      "Enter: open resolution options",
      "R: refresh",
      "Esc: home",
      string.Empty
    };

    if (conflicts.Count == 0)
    {
      AppendOutcome(lines);
      return lines;
    }

    for (int i = 0; i < conflicts.Count; i++)
    {
      SyncConflict conflict = conflicts[i];
      string marker = i == selectedConflictIndex ? ">" : " ";
      lines.Add($"{marker} {conflict.IssueKey} [{conflict.IssueType}] - {conflict.RelativePath}");
      lines.Add($"  {conflict.Summary}");
    }

    SyncConflict selected = conflicts[selectedConflictIndex];
    lines.Add(string.Empty);
    lines.Add($"[INFO] Selected: {selected.IssueKey} ({selected.Operation})");
    if (!string.IsNullOrWhiteSpace(selected.Title))
    {
      lines.Add($"[INFO] Title: {selected.Title}");
    }

    if (!string.IsNullOrWhiteSpace(selected.Details))
    {
      lines.Add("[WARN] Diff preview:");
      foreach (string line in selected.Details.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
      {
        lines.Add(line);
      }
    }

    if (!string.IsNullOrWhiteSpace(screenMessage))
    {
      lines.Add(string.Empty);
      lines.Add(screenMessage);
    }

    return lines;
  }

  private IReadOnlyList<string> BuildResolveLines()
  {
    var lines = new List<string>
    {
      "Arrow keys: choose strategy",
      "Enter: resolve",
      "Esc: back to conflicts",
      string.Empty
    };

    if (conflicts.Count == 0)
    {
      lines.Add("No conflict selected.");
      AppendOutcome(lines);
      return lines;
    }

    SyncConflict conflict = conflicts[selectedConflictIndex];
    lines.Add($"Conflict: {conflict.IssueKey}");
    lines.Add($"File: {conflict.RelativePath}");
    lines.Add($"Type: {conflict.IssueType}");
    lines.Add($"Operation: {conflict.Operation}");
    lines.Add($"Summary: {conflict.Summary}");
    lines.Add(string.Empty);
    lines.Add("Strategy:");

    for (int i = 0; i < resolutionStrategies.Count; i++)
    {
      ConflictResolutionStrategy strategy = resolutionStrategies[i];
      string marker = i == selectedStrategyIndex ? ">" : " ";
      lines.Add($"{marker} {strategy}");
    }

    lines.Add(string.Empty);
    if (!string.IsNullOrWhiteSpace(conflict.Details))
    {
      lines.Add("[WARN] Full diff:");
      foreach (string line in conflict.Details.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
      {
        lines.Add(line);
      }

      lines.Add(string.Empty);
    }

    AppendProgress(lines);
    AppendOutcome(lines);
    return lines;
  }

  private void AppendProgress(List<string> lines)
  {
    OperationProgressState progress = progressSink.GetSnapshot();
    if (string.IsNullOrWhiteSpace(progress.OperationName) && progress.Timeline.Count == 0)
    {
      return;
    }

    lines.Add($"{progressIndicator} {progress.OperationName}");
    if (!string.IsNullOrWhiteSpace(progress.CurrentMessage))
    {
      string stepCounter = progress.TotalSteps > 0
        ? $" ({progress.CompletedSteps}/{progress.TotalSteps})"
        : string.Empty;
      lines.Add($"[STEP] {progress.CurrentMessage}{stepCounter}");
    }

    foreach (string entry in progress.Timeline.TakeLast(4))
    {
      lines.Add(entry);
    }

    lines.Add(string.Empty);
  }

  private void AppendOutcome(List<string> lines)
  {
    if (!string.IsNullOrWhiteSpace(screenMessage))
    {
      lines.Add(screenMessage);
    }

    foreach (string detail in screenDetails)
    {
      lines.Add(detail);
    }
  }

  private IReadOnlyList<CommandDefinition> GetVisibleCommands() =>
    suggestionEngine.Suggest(commandFilter, commandPalette.Commands);

  private static CursorPlacement BuildInputCursorPlacement(string prefix, string value, int lineIndex)
  {
    int top = 2 + lineIndex;
    int left = prefix.Length + value.Length;
    return new CursorPlacement(IsVisible: true, Left: left, Top: top);
  }

  private void MoveHomeSelection(int direction)
  {
    IReadOnlyList<CommandDefinition> commands = GetVisibleCommands();
    if (commands.Count == 0)
    {
      selectedHomeIndex = 0;
      return;
    }

    selectedHomeIndex = (selectedHomeIndex + direction + commands.Count) % commands.Count;
  }

  private void ClampHomeSelection()
  {
    int count = GetVisibleCommands().Count;
    selectedHomeIndex = count == 0 ? 0 : Math.Clamp(selectedHomeIndex, 0, count - 1);
  }

  private void MoveConflictSelection(int direction)
  {
    if (conflicts.Count == 0)
    {
      selectedConflictIndex = 0;
      return;
    }

    selectedConflictIndex = (selectedConflictIndex + direction + conflicts.Count) % conflicts.Count;
  }

  private void MoveStrategySelection(int direction)
  {
    selectedStrategyIndex = (selectedStrategyIndex + direction + resolutionStrategies.Count) % resolutionStrategies.Count;
  }

  private void ReturnHome()
  {
    menuNavigator.SetCurrent(homeScreen);
    ResetHomeState();
  }

  private void ResetHomeState()
  {
    isEditingHomeFilter = false;
    screenMessage = string.Empty;
    screenDetails = [];
    selectedHomeIndex = 0;
    progressIndicator = "[..]";
    progressSink.Reset();
  }
}
