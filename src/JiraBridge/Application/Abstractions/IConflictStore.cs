using JiraBridge.Domain.Sync;

namespace JiraBridge.Application.Abstractions;

public interface IConflictStore
{
  Task<IReadOnlyCollection<SyncConflict>> GetOpenConflictsAsync(CancellationToken cancellationToken);
}
