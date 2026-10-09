using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Strategy.Application.Contracts;
using Agentiva.Strategy.Domain.Entities;

namespace Agentiva.Strategy.Application.Abstractions;

/// <summary>Persistence and read queries for <see cref="Signal"/>, satisfied by Infrastructure.</summary>
public interface ISignalRepository
{
    void Add(Signal signal);

    /// <summary>Most recent signals, newest first, optionally filtered to one symbol.</summary>
    Task<IReadOnlyList<SignalDto>> ListRecentAsync(string? symbol, int limit, CancellationToken cancellationToken);

    /// <summary>Aggregated counts for one strategy.</summary>
    Task<(long Total, long Buys, long Sells, DateTimeOffset? First, DateTimeOffset? Last)> GetCountsAsync(
        StrategyId strategyId, CancellationToken cancellationToken);
}
