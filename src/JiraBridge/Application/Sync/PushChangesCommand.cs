using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;

namespace JiraBridge.Application.Sync;

public sealed record PushChangesCommand(bool DryRun, string? IssueKey = null);

public sealed class PushChangesCommandHandler(ISyncExecutor executor)
{
  public Task<CommandResult> HandleAsync(
    PushChangesCommand command,
    CancellationToken cancellationToken) =>
    executor.PushAsync(command.DryRun, cancellationToken, command.IssueKey);
}
