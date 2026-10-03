using System.ComponentModel.DataAnnotations;
using Agentiva.Trading.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Agentiva.Trading.Infrastructure.Providers;

/// <summary>Paper-trading baseline used until the Portfolio Service holds real state.</summary>
public sealed class PaperPortfolioOptions
{
    public const string SectionName = "PaperPortfolio";

    /// <summary>Starting equity for paper trading, in the quote asset.</summary>
    [Range(typeof(decimal), "0", "100000000")]
    public decimal StartingEquity { get; set; } = 10_000m;
}

/// <summary>
/// Supplies a portfolio snapshot for the risk gate.
/// </summary>
/// <remarks>
/// <para>
/// A Phase 1 implementation returning a configured paper baseline, because the
/// Portfolio Service has no data until Phase 6. From Phase 6 this calls the
/// Portfolio Service over HTTP.
/// </para>
/// <para>
/// It reports zero exposure and zero daily loss, which is the <em>optimistic</em>
/// direction, so the exposure, concentration and daily-loss checks cannot
/// meaningfully bind yet. That is an accepted and documented Phase 1 limitation
/// rather than an oversight — the platform runs in paper mode with no real funds
/// at stake — and it is recorded in docs/architecture/known-limitations.md.
/// </para>
/// </remarks>
public sealed class PaperPortfolioSnapshotProvider(
    IOptions<PaperPortfolioOptions> options,
    ILogger<PaperPortfolioSnapshotProvider> logger)
    : IPortfolioSnapshotProvider
{
    private readonly PaperPortfolioOptions _options = options.Value;
    private bool _warned;

    public Task<PortfolioSnapshotDto> GetAsync(string symbol, CancellationToken cancellationToken)
    {
        if (!_warned)
        {
            _warned = true;

            logger.LogWarning(
                "Using the Phase 1 paper portfolio baseline ({Equity} equity, zero exposure). "
                + "Exposure, concentration and daily-loss checks cannot bind until the Portfolio "
                + "Service supplies real state in Phase 6.",
                _options.StartingEquity);
        }

        return Task.FromResult(new PortfolioSnapshotDto(
            Equity: _options.StartingEquity,
            AvailableBalance: _options.StartingEquity,
            CurrentExposure: 0m,
            CurrentSymbolExposure: 0m,
            OpenPositionCount: 0,
            DailyPnl: 0m));
    }
}

/// <summary>
/// Reports market conditions from the Redis market-data cache.
/// </summary>
/// <remarks>
/// When no cached tick exists the data age is reported as very large rather than
/// zero. Reporting fresh data for data that does not exist would disable the
/// staleness check entirely — the single check that stops the platform sizing a
/// position against a price that no longer holds.
/// </remarks>
public sealed class RedisMarketConditionProvider(
    ILogger<RedisMarketConditionProvider> logger,
    IConnectionMultiplexer? redis = null)
    : IMarketConditionProvider
{
    /// <summary>Redis key template holding the latest tick for a symbol.</summary>
    public const string LastTickKeyTemplate = "agentiva:market:tick:{0}";

    /// <summary>Age reported when nothing is cached: effectively infinitely stale.</summary>
    private const double UnknownDataAgeSeconds = 86_400;

    public async Task<MarketConditionDto> GetAsync(string symbol, CancellationToken cancellationToken)
    {
        if (redis is null or { IsConnected: false })
        {
            logger.LogWarning(
                "Redis is unavailable, so market data for {Symbol} is reported as stale.", symbol);

            return new MarketConditionDto(UnknownDataAgeSeconds, 0m, IsExchangeAvailable: false, null);
        }

        try
        {
            var key = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                LastTickKeyTemplate, symbol);

            var cached = await redis.GetDatabase().StringGetWithExpiryAsync(key);

            if (!cached.Value.HasValue)
            {
                // Nothing cached. Phase 2 populates this from the Binance
                // WebSocket feed; until then every intent is correctly refused
                // for stale data unless a price is supplied explicitly.
                return new MarketConditionDto(UnknownDataAgeSeconds, 0m, IsExchangeAvailable: false, null);
            }

            var tick = System.Text.Json.JsonSerializer.Deserialize<CachedTick>(
                cached.Value.ToString(), BuildingBlocks.Common.Json.AgentivaJson.Options);

            if (tick is null)
            {
                return new MarketConditionDto(UnknownDataAgeSeconds, 0m, IsExchangeAvailable: false, null);
            }

            var age = (DateTimeOffset.UtcNow - tick.ExchangeTimestamp).TotalSeconds;

            return new MarketConditionDto(
                MarketDataAgeSeconds: Math.Max(age, 0),
                VolatilityPercent: tick.VolatilityPercent ?? 0m,
                IsExchangeAvailable: true,
                LastPrice: tick.LastPrice);
        }
        catch (Exception ex) when (ex is RedisException or System.Text.Json.JsonException)
        {
            // Fail safe: an unreadable cache is reported as stale, never fresh.
            logger.LogError(
                ex,
                "Could not read cached market data for {Symbol}. Reporting it as stale.", symbol);

            return new MarketConditionDto(UnknownDataAgeSeconds, 0m, IsExchangeAvailable: false, null);
        }
    }

    /// <summary>Shape of the cached tick written by the Market Data Service.</summary>
    private sealed record CachedTick(
        string Symbol,
        decimal LastPrice,
        decimal? VolatilityPercent,
        DateTimeOffset ExchangeTimestamp);
}
