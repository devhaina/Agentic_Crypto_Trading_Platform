using Agentiva.BuildingBlocks.Common.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Agentiva.Strategy.Infrastructure.Caching;

/// <summary>
/// Writes this service's annualised volatility estimate to the shared Redis
/// key the Trading Service's <c>RedisMarketConditionProvider</c> reads.
/// </summary>
/// <remarks>
/// A dedicated key, never <c>agentiva:market:tick:{symbol}</c> — that key is
/// written by the Market Data Service, and a plain Redis <c>SET</c> replaces
/// a value wholesale, so a second writer sharing it would intermittently
/// clobber the other's fields depending on which service happened to update
/// last. See the remarks on <c>RedisMarketConditionProvider</c> in the
/// Trading Service for the consumer side of this contract.
/// </remarks>
public sealed class VolatilityCache(ILogger<VolatilityCache> logger, IConnectionMultiplexer? redis = null)
{
    /// <summary>Fixed by the consumer in the Trading Service. Do not change independently.</summary>
    public const string KeyTemplate = "agentiva:market:volatility:{0}";

    /// <summary>Matches the Redis TTL the Market Data Service applies to its own latest-value keys.</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task SetAsync(
        string symbol, string timeframe, decimal volatilityPercent, DateTimeOffset computedAt,
        CancellationToken cancellationToken)
    {
        if (redis is null or { IsConnected: false })
        {
            logger.LogWarning(
                "Redis is unavailable; could not cache the volatility estimate for {Symbol}.", symbol);

            return;
        }

        try
        {
            var key = string.Format(System.Globalization.CultureInfo.InvariantCulture, KeyTemplate, symbol);

            var payload = System.Text.Json.JsonSerializer.Serialize(
                new CachedVolatility(symbol, timeframe, volatilityPercent, computedAt), AgentivaJson.Options);

            await redis.GetDatabase().StringSetAsync(key, payload, Ttl);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Failed to cache the volatility estimate for {Symbol}.", symbol);
        }
    }

    private sealed record CachedVolatility(
        string Symbol, string Timeframe, decimal VolatilityPercent, DateTimeOffset ComputedAt);
}
