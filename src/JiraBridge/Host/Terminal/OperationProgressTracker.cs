using JiraBridge.Application.Abstractions;

namespace JiraBridge.Host.Terminal;

public sealed class OperationProgressTracker : IOperationProgressSink
{
  private readonly Lock gate = new();
  private OperationProgressState state = OperationProgressState.Empty;

  public void Start(string operationName, string headline, int totalSteps = 0)
  {
    lock (gate)
    {
      state = new OperationProgressState(
        operationName,
        headline,
        headline,
        0,
        totalSteps,
        true,
        false,
        []);
    }
  }

  public void ReportStep(string message)
  {
    lock (gate)
    {
      OperationProgressState current = EnsureStarted(message);
      int completedSteps = current.TotalSteps > 0
        ? Math.Min(current.CompletedSteps + 1, current.TotalSteps)
        : current.CompletedSteps + 1;

      state = current with
      {
        CurrentMessage = message,
        CompletedSteps = completedSteps,
        Timeline = AppendTimeline(current.Timeline, $"[STEP] {message}")
      };
    }
  }

  public void ReportInfo(string message)
  {
    lock (gate)
    {
      OperationProgressState current = EnsureStarted(message);
      state = current with
      {
        CurrentMessage = message,
        Timeline = AppendTimeline(current.Timeline, $"[INFO] {message}")
      };
    }
  }

  public void Complete(string message)
  {
    lock (gate)
    {
      OperationProgressState current = EnsureStarted(message);
      state = current with
      {
        CurrentMessage = message,
        CompletedSteps = current.TotalSteps > 0 ? current.TotalSteps : current.CompletedSteps,
        IsActive = false,
        IsFailure = false,
        Timeline = AppendTimeline(current.Timeline, $"[OK] {message}")
      };
    }
  }

  public void Fail(string message)
  {
    lock (gate)
    {
      OperationProgressState current = EnsureStarted(message);
      state = current with
      {
        CurrentMessage = message,
        IsActive = false,
        IsFailure = true,
        Timeline = AppendTimeline(current.Timeline, $"[ERR] {message}")
      };
    }
  }

  public OperationProgressState GetSnapshot()
  {
    lock (gate)
    {
      return state with
      {
        Timeline = [.. state.Timeline]
      };
    }
  }

  public void Reset()
  {
    lock (gate)
    {
      state = OperationProgressState.Empty;
    }
  }

  private OperationProgressState EnsureStarted(string message)
  {
    if (!string.IsNullOrWhiteSpace(state.OperationName))
    {
      return state;
    }

    state = new OperationProgressState(
      "Operation",
      message,
      message,
      0,
      0,
      true,
      false,
      []);

    return state;
  }

  private static IReadOnlyList<string> AppendTimeline(IReadOnlyList<string> current, string entry)
  {
    var updated = new List<string>(current.Count + 1);
    updated.AddRange(current);
    updated.Add(entry);
    return updated;
  }
}
