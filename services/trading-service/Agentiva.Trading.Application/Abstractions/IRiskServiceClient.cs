using Agentiva.BuildingBlocks.Common.Results;

namespace Agentiva.Trading.Application.Abstractions;

/// <summary>
/// Synchronous client for the deterministic risk gate.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately synchronous REST rather than an event. The trading workflow
/// cannot continue without a decision, and an asynchronous request/reply over
/// the bus would mean holding a half-created intent while waiting for a
/// correlated reply — more moving parts for no benefit, and a window in which
/// the platform's state is ambiguous.
/// </para>
/// <para>
/// The call is <em>not</em> retried on an ambiguous failure. A timeout may mean
/// the evaluation completed and the response was lost, so a blind retry could
/// produce a second approval for the same intent. The idempotency key makes a
/// deliberate retry safe, but the decision to retry is the caller's, and the
/// Phase 1 caller fails closed instead.
/// </para>
/// </remarks>
public interface IRiskServiceClient
{
    /// <summary>Submits an intent for evaluation.</summary>
    /// <returns>
    /// The decision, or a failure when the gate could not be reached. An
    /// unreachable gate is never interpreted as an approval.
    /// </returns>
    Task<Result<RiskDecisionResult>> EvaluateAsync(
        RiskEvaluationRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

/// <summary>Request sent to the risk gate.</summary>
/// <param name="TradingIntentId">The intent being evaluated.</param>
/// <param name="Symbol">Trading pair.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="EntryPrice">Proposed entry price.</param>
/// <param name="StopLoss">Proposed protective stop.</param>
/// <param name="TakeProfit">Proposed target.</param>
/// <param name="Confidence">Confidence as a fraction, 0 to 1.</param>
/// <param name="Portfolio">Current portfolio state.</param>
/// <param name="SymbolVolatilityPercent">Observed volatility, in percent.</param>
/// <param name="MarketDataAgeSeconds">Age of the latest market data.</param>
/// <param name="IsExchangeAvailable">Whether the exchange is reachable.</param>
/// <param name="HasDuplicateOpenOrder">Whether an equivalent order is already open.</param>
public sealed record RiskEvaluationRequestDto(
    Guid TradingIntentId,
    string Symbol,
    string Side,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal Confidence,
    PortfolioSnapshotDto Portfolio,
    decimal SymbolVolatilityPercent,
    double MarketDataAgeSeconds,
    bool IsExchangeAvailable,
    bool HasDuplicateOpenOrder);

/// <summary>Portfolio state sent to the risk gate.</summary>
/// <param name="Equity">Total account value.</param>
/// <param name="AvailableBalance">Unencumbered balance.</param>
/// <param name="CurrentExposure">Notional of all open positions.</param>
/// <param name="CurrentSymbolExposure">Notional already open in this symbol.</param>
/// <param name="OpenPositionCount">Currently open positions.</param>
/// <param name="DailyPnl">Today's P&amp;L. Negative is a loss.</param>
public sealed record PortfolioSnapshotDto(
    decimal Equity,
    decimal AvailableBalance,
    decimal CurrentExposure,
    decimal CurrentSymbolExposure,
    int OpenPositionCount,
    decimal DailyPnl);

/// <summary>The gate's decision, as the trading workflow sees it.</summary>
/// <param name="RiskCheckId">Identity of the evaluation record.</param>
/// <param name="Decision"><c>Approved</c> or <c>Rejected</c>.</param>
/// <param name="ApprovedQuantity">Quantity cleared. Zero when rejected.</param>
/// <param name="ApprovedNotional">Order value at the effective entry price.</param>
/// <param name="EffectiveEntryPrice">Entry price including slippage.</param>
/// <param name="RiskAmount">Capital at risk if the stop is hit.</param>
/// <param name="RejectionCodes">Codes of every failing check.</param>
public sealed record RiskDecisionResult(
    Guid RiskCheckId,
    string Decision,
    decimal ApprovedQuantity,
    decimal ApprovedNotional,
    decimal EffectiveEntryPrice,
    decimal RiskAmount,
    IReadOnlyList<string> RejectionCodes)
{
    /// <summary>Whether the gate approved the intent.</summary>
    public bool IsApproved => string.Equals(Decision, "Approved", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Supplies the portfolio snapshot the risk gate measures a trade against.
/// </summary>
/// <remarks>
/// Real as of Phase 6: calls the Portfolio Service, which derives every
/// field from actual fills rather than an operator-set baseline. Returns a
/// <see cref="Result"/> rather than a bare DTO, unlike the Phase 1-5
/// signature it replaced, because an unreachable Portfolio Service must be
/// something the caller can fail closed on — a fabricated snapshot would
/// feed the risk gate false confidence instead of no confidence.
/// </remarks>
public interface IPortfolioSnapshotProvider
{
    Task<Result<PortfolioSnapshotDto>> GetAsync(
        Guid tradingAccountId, string symbol, CancellationToken cancellationToken);
}

/// <summary>
/// Supplies market conditions for a symbol: data age, volatility, exchange reachability.
/// </summary>
/// <remarks>
/// Phase 1 reads what the Redis market cache holds and reports data as stale
/// when nothing is there. Reporting "fresh" for absent data would defeat the
/// staleness check entirely, so the absence of data is treated as the worst case.
/// </remarks>
public interface IMarketConditionProvider
{
    Task<MarketConditionDto> GetAsync(string symbol, CancellationToken cancellationToken);
}

/// <summary>Market conditions for a symbol.</summary>
/// <param name="MarketDataAgeSeconds">Age of the latest data. Large when unknown.</param>
/// <param name="VolatilityPercent">Observed annualised volatility, in percent.</param>
/// <param name="IsExchangeAvailable">Whether the exchange is reachable.</param>
/// <param name="LastPrice">Latest known price, if any.</param>
public sealed record MarketConditionDto(
    double MarketDataAgeSeconds,
    decimal VolatilityPercent,
    bool IsExchangeAvailable,
    decimal? LastPrice);

/// <summary>Access to the trading intents this service owns.</summary>
public interface ITradingIntentRepository
{
    void Add(Domain.Intents.TradingIntent intent);

    Task<Domain.Intents.TradingIntent?> GetByIdAsync(
        BuildingBlocks.Domain.Primitives.TradingIntentId id,
        CancellationToken cancellationToken);

    Task<Domain.Intents.TradingIntent?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Domain.Intents.TradingIntent>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken);
}
