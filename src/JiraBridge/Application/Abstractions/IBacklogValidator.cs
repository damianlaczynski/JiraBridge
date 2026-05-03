using JiraBridge.Application.Common;

namespace JiraBridge.Application.Abstractions;

public interface IBacklogValidator
{
  Task<CommandResult> ValidateAsync(CancellationToken cancellationToken);
}
