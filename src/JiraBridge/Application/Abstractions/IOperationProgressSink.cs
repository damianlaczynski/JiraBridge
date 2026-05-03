namespace JiraBridge.Application.Abstractions;

public interface IOperationProgressSink
{
  void Start(string operationName, string headline, int totalSteps = 0);

  void ReportStep(string message);

  void ReportInfo(string message);

  void Complete(string message);

  void Fail(string message);

  OperationProgressState GetSnapshot();

  void Reset();
}

public sealed record OperationProgressState(
  string OperationName,
  string Headline,
  string CurrentMessage,
  int CompletedSteps,
  int TotalSteps,
  bool IsActive,
  bool IsFailure,
  IReadOnlyList<string> Timeline)
{
  public static OperationProgressState Empty { get; } =
    new(string.Empty, string.Empty, string.Empty, 0, 0, false, false, Array.Empty<string>());
}
