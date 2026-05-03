using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;

namespace JiraBridge.Application.Sync;

public sealed record PullChangesCommand(string? IssueKey = null);

public sealed class PullChangesCommandHandler(ISyncExecutor executor)
{
  public Task<CommandResult> HandleAsync(
    PullChangesCommand command,
    CancellationToken cancellationToken) =>
    executor.PullAsync(cancellationToken, command.IssueKey);
}
