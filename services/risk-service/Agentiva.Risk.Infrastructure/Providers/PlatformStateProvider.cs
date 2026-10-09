using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.Application.Configuration;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.Contracts.Events.Operations;
using Agentiva.Risk.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Agentiva.Risk.Infrastructure.Providers;

/// <summary>
/// Reports kill-switch and trading-enabled state.
/// </summary>
/// <remarks>
/// <para>
/// Reads a Redis flag when Redis is available and falls back to configuration
/// otherwise. The fallback direction is the important part: a Redis outage
/// cannot <em>clear</em> the kill switch. If configuration says engaged, it
/// stays engaged, and an unreadable flag is treated as engaged rather than
/// clear.
/// </para>
/// <para>
/// Redis is a cache here, not the source of truth. The durable record of a kill
/// switch activation is the audit event; this flag is the fast path that every
/// risk evaluation reads.
/// </para>
/// </remarks>
public sealed class PlatformStateProvider(
    IOptions<TradingOptions> tradingOptions,
    IEventPublisher publisher,
    ILogger<PlatformStateProvider> logger,
    IConnectionMultiplexer? redis = null)
    : IPlatformStateProvider
{
    /// <summary>Redis key holding the kill switch flag.</summary>
    public const string KillSwitchKey = "agentiva:platform:kill-switch";

    /// <summary>Redis key holding the trading-enabled flag.</summary>
    public const string TradingEnabledKey = "agentiva:platform:trading-enabled";

    private readonly TradingOptions _trading = tradingOptions.Value;

    public TradingMode EffectiveTradingMode => _trading.EffectiveMode;

    public bool IsKillSwitchForcedByConfiguration => _trading.KillSwitchEnabled;

    public async Task<bool> IsKillSwitchEngagedAsync(CancellationToken cancellationToken)
    {
        // Configuration wins when it says "engaged". An operator setting the
        // flag in configuration must not be overridden by a stale cache value.
        if (_trading.KillSwitchEnabled)
        {
            return true;
        }

        if (redis is null or { IsConnected: false })
        {
            return false;
        }

        try
        {
            var value = await redis.GetDatabase().StringGetAsync(KillSwitchKey);
            return value.HasValue && value == "1";
        }
        catch (RedisException ex)
        {
            // Fail safe: an unreadable kill switch is treated as engaged.
            // Refusing to trade on missing information is always recoverable;
            // trading while the switch may be on is not.
            logger.LogError(
                ex,
                "Could not read the kill switch flag from Redis. Treating it as ENGAGED and "
                + "refusing new orders until the state can be established.");

            return true;
        }
    }

    public async Task<bool> IsTradingEnabledAsync(CancellationToken cancellationToken)
    {
        if (redis is null or { IsConnected: false })
        {
            // No cache available: defer to configuration rather than blocking.
            // Trading-enabled differs from the kill switch — it is cleared by
            // reconciliation failures, and its absence is not itself a signal.
            return true;
        }

        try
        {
            var value = await redis.GetDatabase().StringGetAsync(TradingEnabledKey);

            // An unset key means nothing has disabled trading.
            return !value.HasValue || value == "1";
        }
        catch (RedisException ex)
        {
            logger.LogError(
                ex,
                "Could not read the trading-enabled flag from Redis. Treating trading as DISABLED.");

            return false;
        }
    }

    public async Task EngageKillSwitchAsync(
        string trigger, string detail, string activatedBy, CancellationToken cancellationToken)
    {
        logger.LogCritical(
            "KILL SWITCH ENGAGED by {ActivatedBy}. Trigger: {Trigger}. Detail: {Detail}",
            activatedBy, trigger, detail);

        await WriteFlagAsync(KillSwitchKey, engaged: true, cancellationToken);

        // Published directly rather than through the transactional outbox:
        // this provider has no relational write of its own to be atomic with,
        // the same reasoning the Market Data Service documents for its own
        // direct publishes.
        await publisher.PublishAsync(
            new KillSwitchActivated
            {
                CorrelationId = Guid.NewGuid().ToString(),
                Trigger = trigger,
                Detail = detail,
                ActivatedBy = activatedBy,

                // Neither capability exists yet: the Execution Service that
                // would hold resting orders to cancel is Phase 5, and
                // automatic liquidation is never enabled without an
                // independent, explicit risk approval regardless of phase.
                PendingOrdersCancelled = false,
                PositionsLiquidated = false
            },
            cancellationToken);
    }

    public async Task ReleaseKillSwitchAsync(
        string deactivatedBy, string justification, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Kill switch released by {DeactivatedBy}. Justification: {Justification}",
            deactivatedBy, justification);

        await WriteFlagAsync(KillSwitchKey, engaged: false, cancellationToken);

        await publisher.PublishAsync(
            new KillSwitchDeactivated
            {
                CorrelationId = Guid.NewGuid().ToString(),
                DeactivatedBy = deactivatedBy,
                Justification = justification
            },
            cancellationToken);

        if (IsKillSwitchForcedByConfiguration)
        {
            logger.LogWarning(
                "The operator flag was cleared, but Trading:KillSwitchEnabled is still true in "
                + "configuration, so the kill switch remains functionally engaged. A configuration "
                + "change and redeploy is required to actually resume trading.");
        }
    }

    private async Task WriteFlagAsync(string key, bool engaged, CancellationToken cancellationToken)
    {
        if (redis is null or { IsConnected: false })
        {
            // The write has nowhere to land. Logged loudly rather than thrown:
            // the caller already logged the attempt at Critical/Warning above,
            // and the in-process configuration override below still protects
            // an engage request even when Redis cannot record it.
            logger.LogError(
                "Redis is unavailable; could not persist the kill switch flag. If this was an "
                + "engage request, the switch is NOT actually engaged anywhere other services can "
                + "see — only this log line records the attempt.");

            return;
        }

        try
        {
            await redis.GetDatabase().StringSetAsync(key, engaged ? "1" : "0");
        }
        catch (RedisException ex)
        {
            logger.LogError(ex, "Could not write the kill switch flag to Redis.");
        }
    }
}
