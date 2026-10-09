using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.Trading.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Agentiva.Trading.Infrastructure.Providers;

/// <summary>
/// Redis-backed store for the operator-adjustable paper-trading ledger.
/// </summary>
/// <remarks>
/// No TTL on the key: unlike a market-data cache entry, an operator's set
/// ledger value is meant to persist until they change it again, not expire
/// on its own. If Redis is flushed or restarted, <see cref="GetAsync"/>
/// returns <c>null</c> and the caller falls back to the configured baseline —
/// the same fail-safe-default shape as the kill switch, and harmless here
/// specifically because this is paper state with no real funds behind it.
/// </remarks>
public sealed class PaperLedgerStore(IClock clock, ILogger<PaperLedgerStore> logger, IConnectionMultiplexer? redis = null)
    : IPaperLedgerStore
{
    /// <summary>Redis key holding the operator-set ledger.</summary>
    public const string LedgerKey = "agentiva:trading:paper-ledger";

    public async Task<PaperLedgerDto?> GetAsync(CancellationToken cancellationToken)
    {
        if (redis is null or { IsConnected: false })
        {
            return null;
        }

        try
        {
            var value = await redis.GetDatabase().StringGetAsync(LedgerKey);

            return value.HasValue
                ? System.Text.Json.JsonSerializer.Deserialize<PaperLedgerDto>(value.ToString(), AgentivaJson.Options)
                : null;
        }
        catch (Exception ex) when (ex is RedisException or System.Text.Json.JsonException)
        {
            logger.LogError(ex, "Could not read the paper-trading ledger from Redis. Falling back to the configured baseline.");
            return null;
        }
    }

    public async Task SetAsync(
        decimal equity,
        decimal availableBalance,
        decimal currentExposure,
        int openPositionCount,
        decimal dailyPnl,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        var ledger = new PaperLedgerDto(
            equity, availableBalance, currentExposure, openPositionCount, dailyPnl, clock.UtcNow, updatedBy);

        logger.LogWarning(
            "Paper-trading ledger set by {UpdatedBy}: equity {Equity}, available {AvailableBalance}, "
            + "exposure {CurrentExposure}, {OpenPositionCount} open position(s), daily P&L {DailyPnl}.",
            updatedBy, equity, availableBalance, currentExposure, openPositionCount, dailyPnl);

        if (redis is null or { IsConnected: false })
        {
            logger.LogError(
                "Redis is unavailable; the paper-trading ledger could not be persisted. The next "
                + "read will fall back to the configured baseline rather than this value.");

            return;
        }

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(ledger, AgentivaJson.Options);
            await redis.GetDatabase().StringSetAsync(LedgerKey, json);
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Could not write the paper-trading ledger to Redis.");
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        if (redis is null or { IsConnected: false })
        {
            return;
        }

        try
        {
            await redis.GetDatabase().KeyDeleteAsync(LedgerKey);
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Could not clear the paper-trading ledger in Redis.");
        }
    }
}
