using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;

namespace Agentiva.Risk.Application.Evaluations;

/// <summary>
/// Submits a trading intent to the deterministic risk gate.
/// </summary>
/// <remarks>
/// <para>
/// A financial command, so it carries an idempotency key and is guarded by the
/// idempotency behavior: evaluating the same intent twice must return the same
/// decision rather than producing a second approval that could be executed.
/// </para>
/// <para>
/// <b>Phase 1 scope.</b> The portfolio snapshot is supplied by the caller. From
/// Phase 6 the Risk Service fetches it from the Portfolio Service itself, which
/// closes the obvious hole in the current shape — a caller could understate its
/// own exposure. Until then this endpoint is reachable only by the Trading
/// Service and administrators, and the limitation is recorded in
/// docs/architecture/known-limitations.md.
/// </para>
/// </remarks>
/// <param name="IdempotencyKey">Caller-supplied key making the evaluation replay-safe.</param>
/// <param name="TradingIntentId">The intent being evaluated.</param>
/// <param name="Symbol">Trading pair, e.g. <c>BTCUSDT</c>.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="EntryPrice">Proposed entry price.</param>
/// <param name="StopLoss">Proposed protective stop.</param>
/// <param name="TakeProfit">Proposed target.</param>
/// <param name="Confidence">Signal confidence, 0 to 1.</param>
/// <param name="Portfolio">Current portfolio state. See the Phase 1 note above.</param>
/// <param name="SymbolVolatilityPercent">Observed annualised volatility for the symbol.</param>
/// <param name="MarketDataAgeSeconds">Age of the latest market data for the symbol.</param>
/// <param name="IsExchangeAvailable">Whether the exchange is reachable.</param>
/// <param name="HasDuplicateOpenOrder">Whether an equivalent order is already open.</param>
/// <param name="RiskPolicyId">Policy to apply. Defaults to the active default policy.</param>
public sealed record EvaluateIntentCommand(
    string IdempotencyKey,
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
    bool HasDuplicateOpenOrder,
    Guid? RiskPolicyId) : IFinancialCommand<Result<RiskEvaluationResponse>>;

/// <summary>Portfolio state the risk gate measures a trade against.</summary>
/// <param name="Equity">Total account value in the quote asset.</param>
/// <param name="AvailableBalance">Unencumbered quote-asset balance.</param>
/// <param name="CurrentExposure">Notional of all open positions.</param>
/// <param name="CurrentSymbolExposure">Notional already open in this symbol.</param>
/// <param name="OpenPositionCount">Currently open positions.</param>
/// <param name="DailyPnl">Realised plus unrealised P&amp;L today. Negative is a loss.</param>
public sealed record PortfolioSnapshotDto(
    decimal Equity,
    decimal AvailableBalance,
    decimal CurrentExposure,
    decimal CurrentSymbolExposure,
    int OpenPositionCount,
    decimal DailyPnl);

/// <summary>The gate's decision.</summary>
/// <param name="RiskCheckId">Identity of the persisted evaluation record.</param>
/// <param name="TradingIntentId">The intent evaluated.</param>
/// <param name="RiskPolicyId">The policy applied.</param>
/// <param name="Decision"><c>Approved</c> or <c>Rejected</c>.</param>
/// <param name="ApprovedQuantity">
/// Quantity cleared for execution. May be lower than requested; never higher.
/// </param>
/// <param name="ApprovedNotional">Order value at the effective entry price.</param>
/// <param name="EffectiveEntryPrice">Entry price including the slippage assumption.</param>
/// <param name="RiskAmount">Capital at risk if the stop is hit, fees included.</param>
/// <param name="RiskBudget">The budget the size was derived from.</param>
/// <param name="BindingConstraint">Which constraint determined the size.</param>
/// <param name="RejectionCodes">Codes of every failing check. Empty when approved.</param>
/// <param name="Checks">Every check that ran, with its outcome.</param>
public sealed record RiskEvaluationResponse(
    Guid RiskCheckId,
    Guid TradingIntentId,
    Guid RiskPolicyId,
    string Decision,
    decimal ApprovedQuantity,
    decimal ApprovedNotional,
    decimal EffectiveEntryPrice,
    decimal RiskAmount,
    decimal RiskBudget,
    string BindingConstraint,
    IReadOnlyList<string> RejectionCodes,
    IReadOnlyList<RiskCheckDto> Checks);

/// <summary>One check's outcome.</summary>
/// <param name="CheckName">Check identity.</param>
/// <param name="Passed">Whether it passed.</param>
/// <param name="Code">Stable reason code.</param>
/// <param name="Detail">Operator-facing explanation.</param>
/// <param name="ObservedValue">Measured value.</param>
/// <param name="LimitValue">The limit compared against.</param>
public sealed record RiskCheckDto(
    string CheckName,
    bool Passed,
    string Code,
    string Detail,
    string? ObservedValue,
    string? LimitValue);
