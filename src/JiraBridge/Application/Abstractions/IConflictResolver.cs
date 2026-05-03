using JiraBridge.Application.Common;
using JiraBridge.Domain.Sync;

namespace JiraBridge.Application.Abstractions;

public interface IConflictResolver
{
  Task<CommandResult> ResolveAsync(
    string issueKey,
    ConflictResolutionStrategy strategy,
    CancellationToken cancellationToken);
}
