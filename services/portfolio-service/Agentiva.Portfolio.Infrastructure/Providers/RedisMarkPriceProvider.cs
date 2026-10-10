using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.Portfolio.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Agentiva.Portfolio.Infrastructure.Providers;

/// <summary>
/// Reads the Market Data Service's latest-tick cache for a live mark price.
/// </summary>
/// <remarks>
/// Same key and wire shape as <c>Trading.Infrastructure.Providers.RedisMarketConditionProvider</c>
/// — both services read a cache the Market Data Service alone writes. A
/// missing or unreadable cache returns <c>null</c>; the caller falls back to
/// the position's own average entry price, never to a fabricated price.
/// </remarks>
public sealed class RedisMarkPriceProvider(
    ILogger<RedisMarkPriceProvider> logger, IConnectionMultiplexer? redis = null)
    : IMarkPriceProvider
{
    private const string LastTickKeyTemplate = "agentiva:market:tick:{0}";

    public async Task<decimal?> GetMarkPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        if (redis is null or { IsConnected: false })
        {
            return null;
        }

        try
        {
            var key = string.Format(System.Globalization.CultureInfo.InvariantCulture, LastTickKeyTemplate, symbol);
            var cached = await redis.GetDatabase().StringGetAsync(key);

            if (!cached.HasValue)
            {
                return null;
            }

            var tick = System.Text.Json.JsonSerializer.Deserialize<CachedTick>(cached.ToString(), AgentivaJson.Options);
            return tick?.LastPrice;
        }
        catch (Exception ex) when (ex is RedisException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Could not read the cached mark price for {Symbol}.", symbol);
            return null;
        }
    }

    /// <summary>Shape of the cached tick written by the Market Data Service.</summary>
    private sealed record CachedTick(
        string Symbol, decimal LastPrice, decimal? VolatilityPercent, DateTimeOffset ExchangeTimestamp);
}
