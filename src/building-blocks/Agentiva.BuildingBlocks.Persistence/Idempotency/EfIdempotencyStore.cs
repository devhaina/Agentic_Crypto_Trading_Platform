using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Agentiva.BuildingBlocks.Persistence.Idempotency;

/// <summary>EF Core implementation of <see cref="IIdempotencyStore"/>.</summary>
/// <typeparam name="TContext">The service's database context.</typeparam>
public sealed class EfIdempotencyStore<TContext>(TContext context, IClock clock) : IIdempotencyStore
    where TContext : AgentivaDbContext
{
    /// <summary>PostgreSQL SQLSTATE for a unique constraint violation.</summary>
    private const string UniqueViolation = "23505";

    public async Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken)
    {
        var entry = await context.IdempotencyEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Key == key, cancellationToken);

        return entry is null
            ? null
            : new IdempotencyRecord(
                entry.Key,
                entry.RequestType,
                entry.State,
                entry.ResponsePayload,
                entry.FailureReason,
                entry.CreatedAt);
    }

    public async Task<bool> TryClaimAsync(string key, string requestType, CancellationToken cancellationToken)
    {
        var entry = new IdempotencyEntry
        {
            Key = key,
            RequestType = requestType,
            State = IdempotencyState.InFlight,
            CreatedAt = clock.UtcNow
        };

        context.IdempotencyEntries.Add(entry);

        try
        {
            // Committed immediately and on its own, deliberately. The claim must
            // be visible to other replicas before the handler runs, so it cannot
            // ride along with the handler's transaction — two concurrent
            // requests would then both see no claim and both proceed.
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another caller won the race. The unique index on key is what makes
            // this safe: exactly one insert can succeed.
            context.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }

    public async Task CompleteAsync(string key, string? responsePayload, CancellationToken cancellationToken)
    {
        var entry = await context.IdempotencyEntries.FirstOrDefaultAsync(e => e.Key == key, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Cannot complete idempotency key '{key}': no claim exists.");

        entry.State = IdempotencyState.Completed;
        entry.ResponsePayload = responsePayload;
        entry.CompletedAt = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(string key, string failureReason, CancellationToken cancellationToken)
    {
        var entry = await context.IdempotencyEntries.FirstOrDefaultAsync(e => e.Key == key, cancellationToken);

        if (entry is null)
        {
            // Nothing to poison. The claim never landed, so a retry is safe.
            return;
        }

        entry.State = IdempotencyState.Failed;
        entry.FailureReason = failureReason.Length > 2000 ? failureReason[..2000] : failureReason;
        entry.CompletedAt = clock.UtcNow;

        // Written even while the surrounding request is failing: without the
        // record, a retry would re-run a command whose outcome is unknown.
        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: UniqueViolation };
}
