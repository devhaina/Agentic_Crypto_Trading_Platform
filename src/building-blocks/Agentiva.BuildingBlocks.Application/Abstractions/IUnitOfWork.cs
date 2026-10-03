namespace Agentiva.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Commits a set of changes, together with the domain events they raised, as one
/// atomic unit.
/// </summary>
/// <remarks>
/// Implementations write pending domain events into the outbox table inside the
/// same transaction as the state change. That removes the failure mode where an
/// order is persisted but its <c>order.created</c> event is lost because the
/// broker was briefly unavailable — or the mirror image, where the event is
/// published and the transaction then rolls back.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Persists changes and enqueues raised domain events atomically.</summary>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
