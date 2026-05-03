using JiraBridge.Domain.Configuration;

namespace JiraBridge.Application.Abstractions;

public interface IRepositoryMetadataRefresher
{
  Task<RepositoryJiraConfiguration> RefreshAsync(
    string repoRoot,
    RepositorySettings repositorySettings,
    CancellationToken cancellationToken);
}
