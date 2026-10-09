using System.Collections.Concurrent;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;
using Agentiva.MarketData.Domain.Staleness;
using Agentiva.MarketData.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Agentiva.MarketData.Infrastructure.Ingestion;

/// <summary>
/// Live, in-memory freshness state for every configured symbol.
/// </summary>
/// <remarks>
/// A singleton touched by every <see cref="Binance.BinanceStreamConnection{TPayload}"/>
/// for a symbol (ticker, trade, kline, depth) and read by the status query and
/// <see cref="StalenessMonitorWorker"/>. Deliberately in-memory rather than
/// Redis: this state describes <em>this process's</em> live sockets, which has
/// no meaning to another replica and would be wrong to read from one.
/// </remarks>
public sealed class MarketFeedState(IOptions<MarketDataOptions> options, IClock clock) : IMarketFeedStatusProvider
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastMessageAt = new(StringComparer.Ordinal);

    /// <summary>Records that a message was just received for a symbol, on any stream.</summary>
    public void Touch(string symbol) => _lastMessageAt[symbol] = clock.UtcNow;

    public IReadOnlyList<SymbolFeedStatusDto> GetStatus()
    {
        var now = clock.UtcNow;
        var threshold = TimeSpan.FromSeconds(options.Value.StalenessThresholdSeconds);

        return options.Value.SymbolList
            .Select(symbol =>
            {
                var hasData = _lastMessageAt.TryGetValue(symbol, out var lastMessageAt);

                var staleness = hasData
                    ? StalenessEvaluator.Evaluate(lastMessageAt, now, threshold)
                    : new StalenessStatus(IsStale: true, StaleFor: TimeSpan.Zero);

                // "Connected" is reported as "actively delivering fresh data"
                // rather than tracking each stream's raw socket state. A
                // connection that is technically open but has stopped
                // delivering messages is, for every purpose this status
                // serves, indistinguishable from a dead one.
                return new SymbolFeedStatusDto(symbol, IsConnected: hasData && !staleness.IsStale,
                    hasData ? lastMessageAt : null, staleness.IsStale, staleness.StaleFor.TotalSeconds);
            })
            .ToArray();
    }
}
