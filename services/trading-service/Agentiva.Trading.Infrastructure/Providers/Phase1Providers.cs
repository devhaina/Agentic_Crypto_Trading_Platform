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
/// Phase 4 replaced the Phase 1 hardcoded baseline with an operator-adjustable
/// one: <see cref="GetAsync"/> reads <see cref="IPaperLedgerStore"/> first —
/// a Redis-backed value an operator can set through an authenticated endpoint
/// — and falls back to <see cref="ConfiguredBaseline"/> only when nothing has
/// been set. This is still simulated state, not a real portfolio: nothing
/// here tracks fills or derives exposure from actual trades. The Portfolio
/// Service has no data until Phase 6, and this provider is replaced outright
/// then, not extended.
/// </para>
/// <para>
/// <c>CurrentSymbolExposure</c> on the returned snapshot is always
/// zero regardless of source — a real per-symbol figure needs real position
/// tracking, which this ledger deliberately does not attempt. See
/// docs/architecture/known-limitations.md.
/// </para>
/// </remarks>
public sealed class PaperPortfolioSnapshotProvider(
    IOptions<PaperPortfolioOptions> options,
    IPaperLedgerStore ledgerStore,
    ILogger<PaperPortfolioSnapshotProvider> logger)
    : IPortfolioSnapshotProvider, IPaperLedgerDefaults
{
    private readonly PaperPortfolioOptions _options = options.Value;
    private bool _warned;

    public async Task<PortfolioSnapshotDto> GetAsync(string symbol, CancellationToken cancellationToken)
    {
        var ledger = await ledgerStore.GetAsync(cancellationToken);

        if (ledger is null)
        {
            if (!_warned)
            {
                _warned = true;

                logger.LogWarning(
                    "No operator-set paper ledger exists; using the configured baseline ({Equity} "
                    + "equity, zero exposure). Set one with PUT /api/v1/trading/paper-ledger.",
                    _options.StartingEquity);
            }

            ledger = ConfiguredBaseline();
        }

        return new PortfolioSnapshotDto(
            Equity: ledger.Equity,
            AvailableBalance: ledger.AvailableBalance,
            CurrentExposure: ledger.CurrentExposure,
            CurrentSymbolExposure: 0m,
            OpenPositionCount: ledger.OpenPositionCount,
            DailyPnl: ledger.DailyPnl);
    }

    public PaperLedgerDto ConfiguredBaseline() => new(
        Equity: _options.StartingEquity,
        AvailableBalance: _options.StartingEquity,
        CurrentExposure: 0m,
        OpenPositionCount: 0,
        DailyPnl: 0m,
        UpdatedAt: default,
        UpdatedBy: "SYSTEM");
}

/// <summary>
/// Reports market conditions from the Redis market-data cache.
/// </summary>
/// <remarks>
/// <para>
/// When no cached tick exists the data age is reported as very large rather than
/// zero. Reporting fresh data for data that does not exist would disable the
/// staleness check entirely — the single check that stops the platform sizing a
/// position against a price that no longer holds.
/// </para>
/// <para>
/// Price and volatility come from two independently-written keys, not one.
/// The Market Data Service owns <see cref="LastTickKeyTemplate"/> (price and
/// timestamp); the Strategy Service owns <see cref="VolatilityKeyTemplate"/>
/// (an ATR-derived annualised estimate, computed once it has enough candle
/// history — see <c>IndicatorEngine</c> and <c>MarketCandleCreatedHandler</c>
/// in that service, Phase 4). Two writers sharing one key would race on every
/// update, since a plain Redis <c>SET</c> replaces the whole value; two
/// independent keys make that impossible by construction, at the cost of
/// this provider needing two reads instead of one.
/// </para>
/// </remarks>
public sealed class RedisMarketConditionProvider(
    ILogger<RedisMarketConditionProvider> logger,
    IConnectionMultiplexer? redis = null)
    : IMarketConditionProvider
{
    /// <summary>Redis key template holding the latest tick for a symbol. Written by the Market Data Service.</summary>
    public const string LastTickKeyTemplate = "agentiva:market:tick:{0}";

    /// <summary>Redis key template holding the latest volatility estimate for a symbol. Written by the Strategy Service.</summary>
    public const string VolatilityKeyTemplate = "agentiva:market:volatility:{0}";

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

        var volatilityPercent = await ReadVolatilityAsync(symbol, cancellationToken);

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
                return new MarketConditionDto(UnknownDataAgeSeconds, volatilityPercent, IsExchangeAvailable: false, null);
            }

            var tick = System.Text.Json.JsonSerializer.Deserialize<CachedTick>(
                cached.Value.ToString(), BuildingBlocks.Common.Json.AgentivaJson.Options);

            if (tick is null)
            {
                return new MarketConditionDto(UnknownDataAgeSeconds, volatilityPercent, IsExchangeAvailable: false, null);
            }

            var age = (DateTimeOffset.UtcNow - tick.ExchangeTimestamp).TotalSeconds;

            return new MarketConditionDto(
                MarketDataAgeSeconds: Math.Max(age, 0),
                VolatilityPercent: volatilityPercent,
                IsExchangeAvailable: true,
                LastPrice: tick.LastPrice);
        }
        catch (Exception ex) when (ex is RedisException or System.Text.Json.JsonException)
        {
            // Fail safe: an unreadable cache is reported as stale, never fresh.
            logger.LogError(
                ex,
                "Could not read cached market data for {Symbol}. Reporting it as stale.", symbol);

            return new MarketConditionDto(UnknownDataAgeSeconds, volatilityPercent, IsExchangeAvailable: false, null);
        }
    }

    /// <summary>
    /// Reads the Strategy Service's volatility estimate. Absent (no candle
    /// history computed yet) or unreadable both fall back to zero — the same
    /// optimistic-direction default this field has always reported before
    /// Phase 4, when nothing computed it at all.
    /// </summary>
    private async Task<decimal> ReadVolatilityAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var key = string.Format(
                System.Globalization.CultureInfo.InvariantCulture, VolatilityKeyTemplate, symbol);

            var cached = await redis!.GetDatabase().StringGetAsync(key);

            if (!cached.HasValue)
            {
                return 0m;
            }

            var snapshot = System.Text.Json.JsonSerializer.Deserialize<CachedVolatility>(
                cached.ToString(), BuildingBlocks.Common.Json.AgentivaJson.Options);

            return snapshot?.VolatilityPercent ?? 0m;
        }
        catch (Exception ex) when (ex is RedisException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Could not read the cached volatility estimate for {Symbol}.", symbol);
            return 0m;
        }
    }

    /// <summary>Shape of the cached tick written by the Market Data Service.</summary>
    private sealed record CachedTick(
        string Symbol,
        decimal LastPrice,
        decimal? VolatilityPercent,
        DateTimeOffset ExchangeTimestamp);

    /// <summary>Shape of the cached volatility estimate written by the Strategy Service.</summary>
    private sealed record CachedVolatility(
        string Symbol,
        string Timeframe,
        decimal VolatilityPercent,
        DateTimeOffset ComputedAt);
}
