namespace JiraBridge.Application.Common;

public sealed record CommandResult(bool Success, string Message, IReadOnlyList<string>? Details = null)
{
  public static CommandResult Ok(string message, params string[] details) => new(true, message, details);

  public static CommandResult Fail(string message, params string[] details) => new(false, message, details);
}
