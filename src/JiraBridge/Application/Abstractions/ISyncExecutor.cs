using JiraBridge.Application.Common;

namespace JiraBridge.Application.Abstractions;

public interface ISyncExecutor
{
  Task<CommandResult> PushAsync(bool dryRun, CancellationToken cancellationToken);

  Task<CommandResult> PullAsync(CancellationToken cancellationToken);
}
