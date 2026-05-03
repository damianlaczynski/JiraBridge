using JiraBridge.Application.Common;

namespace JiraBridge.Application.Abstractions;

public interface IRepositoryBootstrapper
{
  Task<CommandResult> ConfigureAsync(string projectKey, CancellationToken cancellationToken);
}
