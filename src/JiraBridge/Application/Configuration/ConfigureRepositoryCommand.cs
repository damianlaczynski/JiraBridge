using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;

namespace JiraBridge.Application.Configuration;

public sealed record ConfigureRepositoryCommand(string ProjectKey);

public sealed class ConfigureRepositoryCommandHandler(IRepositoryBootstrapper bootstrapper)
{
  public Task<CommandResult> HandleAsync(
    ConfigureRepositoryCommand command,
    CancellationToken cancellationToken) =>
    bootstrapper.ConfigureAsync(command.ProjectKey, cancellationToken);
}
