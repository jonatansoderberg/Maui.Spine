using Orientera.Domain;

namespace Orientera.Services.Offline;

/// <summary>
/// Local persistence for competition packages. Deliberately narrow: M1 stores a handful of
/// packages, so a document-per-competition store is enough. It moves to SQLite when the data
/// volume — a full season of results and splits — justifies querying rather than loading.
/// </summary>
public interface IOfflineStore
{
    Task<CompetitionPackage?> GetAsync(CompetitionId competition, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompetitionPackage>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CompetitionPackage package, CancellationToken cancellationToken = default);

    Task RemoveAsync(CompetitionId competition, CancellationToken cancellationToken = default);
}
