using JiraBridge.Application.Common;

namespace JiraBridge.Application.Abstractions;

public interface ISyncExecutor
{
  Task<CommandResult> PushAsync(bool dryRun, CancellationToken cancellationToken, string? issueKeyFilter = null);

  Task<CommandResult> PullAsync(CancellationToken cancellationToken, string? issueKeyFilter = null);
}
