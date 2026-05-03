using JiraBridge.Application.Abstractions;
using JiraBridge.Domain.Configuration;

namespace JiraBridge.UnitTests.Support;

public sealed class FakeRepositoryMetadataRefresher : IRepositoryMetadataRefresher
{
  private readonly Func<string, RepositorySettings, CancellationToken, Task<RepositoryJiraConfiguration>> implementation;

  public FakeRepositoryMetadataRefresher(Func<string, RepositorySettings, CancellationToken, Task<RepositoryJiraConfiguration>> implementation)
  {
    this.implementation = implementation;
  }

  public Task<RepositoryJiraConfiguration> RefreshAsync(
    string repoRoot,
    RepositorySettings repositorySettings,
    CancellationToken cancellationToken) =>
    implementation(repoRoot, repositorySettings, cancellationToken);
}
