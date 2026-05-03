using JiraBridge.Application.Abstractions;
using JiraBridge.Domain.Sync;

namespace JiraBridge.Application.Sync;

public sealed record GetConflictsQuery;

public sealed class GetConflictsQueryHandler(IConflictStore conflictStore)
{
  public Task<IReadOnlyCollection<SyncConflict>> HandleAsync(
    GetConflictsQuery query,
    CancellationToken cancellationToken) =>
    conflictStore.GetOpenConflictsAsync(cancellationToken);
}
