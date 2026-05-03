using JiraBridge.Application.Abstractions;
using JiraBridge.Domain.Configuration;
using JiraBridge.Infrastructure.Repository;

namespace JiraBridge.UnitTests.Support;

public sealed class LocalFixtureMetadataRefresher : IRepositoryMetadataRefresher
{
  public Task<RepositoryJiraConfiguration> RefreshAsync(
    string repoRoot,
    RepositorySettings repositorySettings,
    CancellationToken cancellationToken)
  {
    _ = cancellationToken;
    RepositoryJiraConfiguration? configuration =
      RepositoryJiraConfigurationStore.TryLoad(repoRoot, repositorySettings, out string? error);
    if (configuration is null)
    {
      throw new InvalidOperationException(error ?? "Could not load project metadata fixture.");
    }

    return Task.FromResult(configuration);
  }
}
