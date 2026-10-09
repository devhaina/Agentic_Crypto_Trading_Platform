using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Agentiva.MarketData.Infrastructure.Caching;

/// <summary>Redis-backed <see cref="IMarketDataCache"/>.</summary>
/// <remarks>
/// Every key carries a TTL rather than living forever. If this service stops
/// ingesting — a crash, a long outage — a cached value past its TTL
/// disappears and callers correctly fall back to the TimescaleDB history
/// (and from there to "no data"), instead of an ingestion outage being masked
/// by an arbitrarily old price sitting in the cache forever.
/// </remarks>
public sealed class RedisMarketDataCache(IConnectionMultiplexer redis, ILogger<RedisMarketDataCache> logger)
    : IMarketDataCache
{
    private static readonly TimeSpan LatestValueTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CandleTtl = TimeSpan.FromHours(25);

    private IDatabase Database => redis.GetDatabase();

    public Task SetLatestTickerAsync(TickerDto ticker, CancellationToken cancellationToken)
    {
        var internalWrite = SetAsync(TickerKey(ticker.Symbol), ticker, LatestValueTtl);

        // The contract RedisMarketConditionProvider (Trading Service) and the
        // Risk Service's market-condition checks already depend on. Key and
        // shape are fixed by that consumer — see
        // services/trading-service/Agentiva.Trading.Infrastructure/Providers/Phase1Providers.cs
        // — and must not drift without a coordinated change on both sides.
        // VolatilityPercent is deliberately null: no component in Phase 2
        // computes a volatility estimate. The indicator engine lands in
        // Phase 3; until then the risk check that reads this field treats a
        // missing value as zero, which is documented in
        // docs/architecture/known-limitations.md as an accepted Phase 2 gap,
        // not an oversight.
        var crossServiceWrite = SetAsync(
            CrossServiceTickKey(ticker.Symbol),
            new CrossServiceCachedTick(ticker.Symbol, ticker.LastPrice, null, ticker.ExchangeTimestamp),
            LatestValueTtl);

        return Task.WhenAll(internalWrite, crossServiceWrite);
    }

    public async Task<TickerDto?> GetLatestTickerAsync(string symbol, CancellationToken cancellationToken)
        => await GetAsync<TickerDto>(TickerKey(symbol));

    public Task SetLatestCandleAsync(CandleDto candle, CancellationToken cancellationToken)
        => SetAsync(CandleKey(candle.Symbol, candle.Timeframe), candle, CandleTtl);

    public async Task<CandleDto?> GetLatestCandleAsync(string symbol, string timeframe, CancellationToken cancellationToken)
        => await GetAsync<CandleDto>(CandleKey(symbol, timeframe));

    public Task SetLatestOrderBookAsync(OrderBookDto orderBook, CancellationToken cancellationToken)
        => SetAsync(OrderBookKey(orderBook.Symbol), orderBook, LatestValueTtl);

    public async Task<OrderBookDto?> GetLatestOrderBookAsync(string symbol, CancellationToken cancellationToken)
        => await GetAsync<OrderBookDto>(OrderBookKey(symbol));

    private static string TickerKey(string symbol) => $"agentiva:marketdata:ticker:{symbol.ToUpperInvariant()}";

    private static string CandleKey(string symbol, string timeframe)
        => $"agentiva:marketdata:candle:{symbol.ToUpperInvariant()}:{timeframe}";

    private static string OrderBookKey(string symbol) => $"agentiva:marketdata:orderbook:{symbol.ToUpperInvariant()}";

    /// <summary>Fixed by the consumer in the Trading Service. Do not change independently.</summary>
    private static string CrossServiceTickKey(string symbol) => $"agentiva:market:tick:{symbol.ToUpperInvariant()}";

    private async Task SetAsync<T>(string key, T value, TimeSpan ttl)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(value, AgentivaJson.Options);
            await Database.StringSetAsync(key, json, ttl);
        }
        catch (RedisException ex)
        {
            // A cache write failure must not stop ingestion: the value is
            // still landing in TimescaleDB, and the read side falls back to
            // it on a cache miss.
            logger.LogWarning(ex, "Failed to write market data cache key {Key}.", key);
        }
    }

    private async Task<T?> GetAsync<T>(string key)
        where T : class
    {
        try
        {
            var value = await Database.StringGetAsync(key);
            return value.HasValue
                ? System.Text.Json.JsonSerializer.Deserialize<T>(value.ToString(), AgentivaJson.Options)
                : null;
        }
        catch (Exception ex) when (ex is RedisException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Failed to read market data cache key {Key}.", key);
            return null;
        }
    }

    /// <summary>
    /// Shape of the shared tick cache entry consumed by
    /// <c>RedisMarketConditionProvider</c> in the Trading Service.
    /// </summary>
    private sealed record CrossServiceCachedTick(
        string Symbol, decimal LastPrice, decimal? VolatilityPercent, DateTimeOffset ExchangeTimestamp);
}
