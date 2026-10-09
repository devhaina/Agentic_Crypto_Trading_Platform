using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Strategy.Application.Abstractions;
using Agentiva.Strategy.Application.Contracts;
using Agentiva.Strategy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Strategy.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IStrategyDefinitionRepository"/>.</summary>
public sealed class StrategyDefinitionRepository(StrategyDbContext context) : IStrategyDefinitionRepository
{
    public void Add(StrategyDefinition strategy) => context.Strategies.Add(strategy);

    public Task<StrategyDefinition?> GetByNameAsync(string name, CancellationToken cancellationToken)
        => context.Strategies.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);

    public Task<StrategyDefinition?> GetByIdAsync(StrategyId id, CancellationToken cancellationToken)
        => context.Strategies.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<StrategyDefinition>> ListActiveAsync(CancellationToken cancellationToken)
        => await context.Strategies.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StrategyDefinition>> ListAllAsync(CancellationToken cancellationToken)
        => await context.Strategies.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);
}

/// <summary>EF Core implementation of <see cref="ISignalRepository"/>.</summary>
public sealed class SignalRepository(StrategyDbContext context) : ISignalRepository
{
    public void Add(Signal signal) => context.Signals.Add(signal);

    public async Task<IReadOnlyList<SignalDto>> ListRecentAsync(
        string? symbol, int limit, CancellationToken cancellationToken)
    {
        var query = context.Signals.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var normalised = Symbol.Create(symbol);
            query = query.Where(s => s.Symbol == normalised);
        }

        var rows = await query
            .OrderByDescending(s => s.ComputedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToArray();
    }

    public async Task<(long Total, long Buys, long Sells, DateTimeOffset? First, DateTimeOffset? Last)> GetCountsAsync(
        StrategyId strategyId, CancellationToken cancellationToken)
    {
        var query = context.Signals.AsNoTracking().Where(s => s.StrategyId == strategyId);

        var total = await query.LongCountAsync(cancellationToken);

        if (total == 0)
        {
            return (0, 0, 0, null, null);
        }

        var buys = await query.LongCountAsync(s => s.Action == TradeAction.Buy, cancellationToken);
        var sells = await query.LongCountAsync(s => s.Action == TradeAction.Sell, cancellationToken);
        var first = await query.MinAsync(s => s.ComputedAt, cancellationToken);
        var last = await query.MaxAsync(s => s.ComputedAt, cancellationToken);

        return (total, buys, sells, first, last);
    }

    private static SignalDto ToDto(Signal signal) => new(
        signal.Id.Value,
        signal.StrategyId.Value,
        signal.StrategyName,
        signal.StrategyVersion,
        signal.Symbol.Value,
        signal.Timeframe,
        signal.Action.ToString().ToUpperInvariant(),
        signal.Confidence.AsFraction,
        signal.EntryPrice.Value,
        signal.StopLoss,
        signal.TakeProfit,
        signal.ReasonCodes,
        signal.ComputedAt);
}
