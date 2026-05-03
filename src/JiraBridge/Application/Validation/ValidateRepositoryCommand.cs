using JiraBridge.Application.Abstractions;
using JiraBridge.Application.Common;

namespace JiraBridge.Application.Validation;

public sealed record ValidateRepositoryCommand;

public sealed class ValidateRepositoryCommandHandler(IBacklogValidator validator)
{
  public Task<CommandResult> HandleAsync(
    ValidateRepositoryCommand command,
    CancellationToken cancellationToken) =>
    validator.ValidateAsync(cancellationToken);
}
