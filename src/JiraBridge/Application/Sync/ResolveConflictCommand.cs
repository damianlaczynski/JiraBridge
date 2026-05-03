using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;
using JiraBridge.Domain.Sync;

namespace JiraBridge.Application.Sync;

public sealed record ResolveConflictCommand(string IssueKey, ConflictResolutionStrategy Strategy);

public sealed class ResolveConflictCommandHandler(IConflictResolver conflictResolver)
{
  public Task<CommandResult> HandleAsync(
    ResolveConflictCommand command,
    CancellationToken cancellationToken) =>
    conflictResolver.ResolveAsync(command.IssueKey, command.Strategy, cancellationToken);
}
