using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Trading.Application.Abstractions;
using Agentiva.Trading.Domain.Intents;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Trading.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="ITradingIntentRepository"/>.</summary>
public sealed class TradingIntentRepository(TradingDbContext context) : ITradingIntentRepository
{
    public void Add(TradingIntent intent) => context.TradingIntents.Add(intent);

    public Task<TradingIntent?> GetByIdAsync(TradingIntentId id, CancellationToken cancellationToken)
        => context.TradingIntents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<TradingIntent?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
        => context.TradingIntents
            .FirstOrDefaultAsync(i => i.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<TradingIntent>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken)
        => await context.TradingIntents
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
}
