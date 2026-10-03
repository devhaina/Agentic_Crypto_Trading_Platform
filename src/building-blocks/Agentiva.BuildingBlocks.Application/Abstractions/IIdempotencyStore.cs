namespace Agentiva.BuildingBlocks.Application.Abstractions;

/// <summary>Lifecycle state of an idempotent operation.</summary>
public enum IdempotencyState
{
    /// <summary>Claimed and executing. A concurrent replay must be rejected, not run.</summary>
    InFlight = 1,

    /// <summary>Finished. A replay returns the recorded response.</summary>
    Completed = 2,

    /// <summary>
    /// Threw before completing. The outcome is unknown, so a replay is refused
    /// until an operator or the reconciliation service resolves it.
    /// </summary>
    Failed = 3
}

/// <summary>A recorded idempotent operation.</summary>
/// <param name="Key">The caller-supplied idempotency key.</param>
/// <param name="RequestType">CLR type name of the originating command.</param>
/// <param name="State">Current lifecycle state.</param>
/// <param name="ResponsePayload">Serialised response, present once completed.</param>
/// <param name="FailureReason">Failure detail, present once failed.</param>
/// <param name="CreatedAt">When the key was first claimed.</param>
public sealed record IdempotencyRecord(
    string Key,
    string RequestType,
    IdempotencyState State,
    string? ResponsePayload,
    string? FailureReason,
    DateTimeOffset CreatedAt);

/// <summary>
/// Durable record of financial commands already executed, keyed by idempotency key.
/// </summary>
/// <remarks>
/// Backed by a uniquely indexed PostgreSQL table, not Redis. Redis is a cache in
/// this platform and may lose a key on eviction or failover; losing an
/// idempotency key means a duplicate exchange order, so the record lives in the
/// same durable store — and the same transaction — as the state it guards.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>Returns the record for a key, or <c>null</c> if the key is unseen.</summary>
    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims a key.
    /// </summary>
    /// <returns>
    /// <c>true</c> when this caller claimed the key; <c>false</c> when another
    /// caller already holds it. Relies on a unique index rather than a
    /// read-then-write, so two concurrent requests cannot both succeed.
    /// </returns>
    Task<bool> TryClaimAsync(string key, string requestType, CancellationToken cancellationToken);

    /// <summary>Marks a claimed key completed and stores the response for replay.</summary>
    Task CompleteAsync(string key, string? responsePayload, CancellationToken cancellationToken);

    /// <summary>Marks a claimed key failed with an unknown outcome.</summary>
    Task MarkFailedAsync(string key, string failureReason, CancellationToken cancellationToken);
}
