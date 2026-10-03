using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Domain.Policies;
using Agentiva.Risk.Domain.Sizing;

namespace Agentiva.Risk.Domain.Evaluation;

/// <summary>Everything the risk gate needs to decide on one trade.</summary>
/// <remarks>
/// A single immutable input record, assembled by the application layer from the
/// portfolio service, the market data cache and the configuration service. The
/// evaluator itself performs no I/O: given the same request it always returns
/// the same decision, which is what makes the gate auditable and unit-testable.
/// </remarks>
/// <param name="TradingIntentId">The intent under evaluation.</param>
/// <param name="Symbol">Trading pair.</param>
/// <param name="Side">Trade direction.</param>
/// <param name="EntryPrice">Proposed entry price.</param>
/// <param name="StopLoss">Proposed protective stop, if any.</param>
/// <param name="TakeProfit">Proposed target, if any.</param>
/// <param name="Confidence">Signal or proposal confidence.</param>
/// <param name="Policy">The risk policy in force.</param>
/// <param name="Precision">Exchange filters for the symbol.</param>
/// <param name="PortfolioEquity">Total account value.</param>
/// <param name="AvailableBalance">Unencumbered quote-asset balance.</param>
/// <param name="CurrentExposure">Notional of all open positions.</param>
/// <param name="CurrentSymbolExposure">Notional already open in this symbol.</param>
/// <param name="OpenPositionCount">Currently open positions.</param>
/// <param name="DailyPnl">Realised plus unrealised P&amp;L for the current UTC day. Negative is a loss.</param>
/// <param name="SymbolVolatility">Observed annualised volatility for the symbol.</param>
/// <param name="MarketDataAge">Age of the most recent market data for the symbol.</param>
/// <param name="IsExchangeAvailable">Whether the exchange is reachable.</param>
/// <param name="IsKillSwitchEngaged">Whether the global kill switch is engaged.</param>
/// <param name="IsTradingEnabled">Whether new order admission is enabled.</param>
/// <param name="TradingMode">Effective trading mode.</param>
/// <param name="HasDuplicateOpenOrder">
/// Whether an equivalent order is already open for this symbol and side.
/// </param>
/// <param name="EvaluatedAt">Evaluation timestamp.</param>
public sealed record RiskEvaluationRequest(
    Guid TradingIntentId,
    Symbol Symbol,
    OrderSide Side,
    Price EntryPrice,
    Price? StopLoss,
    Price? TakeProfit,
    Percentage Confidence,
    RiskPolicy Policy,
    InstrumentPrecision Precision,
    Money PortfolioEquity,
    Money AvailableBalance,
    Money CurrentExposure,
    Money CurrentSymbolExposure,
    int OpenPositionCount,
    Money DailyPnl,
    Percentage SymbolVolatility,
    TimeSpan MarketDataAge,
    bool IsExchangeAvailable,
    bool IsKillSwitchEngaged,
    bool IsTradingEnabled,
    TradingMode TradingMode,
    bool HasDuplicateOpenOrder,
    DateTimeOffset EvaluatedAt);

/// <summary>Result of one named check.</summary>
/// <param name="CheckName">Check identity from <see cref="RiskCheckNames"/>.</param>
/// <param name="Passed">Whether it passed.</param>
/// <param name="Code">Stable reason code from <see cref="RiskCheckCodes"/>.</param>
/// <param name="Detail">Operator-facing explanation.</param>
/// <param name="ObservedValue">Measured value, as text to preserve decimal precision.</param>
/// <param name="LimitValue">The limit compared against.</param>
public sealed record RiskCheckOutcome(
    string CheckName,
    bool Passed,
    string Code,
    string Detail,
    string? ObservedValue = null,
    string? LimitValue = null)
{
    public static RiskCheckOutcome Pass(string checkName, string? observed = null, string? limit = null)
        => new(checkName, true, RiskCheckCodes.Approved, "Passed.", observed, limit);

    public static RiskCheckOutcome Fail(
        string checkName, string code, string detail, string? observed = null, string? limit = null)
        => new(checkName, false, code, detail, observed, limit);
}

/// <summary>The gate's decision on one trade.</summary>
/// <param name="Decision">Approved or rejected.</param>
/// <param name="Checks">Every check that ran, in order. The full audit basis.</param>
/// <param name="RejectionCodes">Codes of all failing checks. Empty when approved.</param>
/// <param name="Sizing">
/// The sizing calculation. Present when sizing ran, even on rejection, so an
/// operator can see what size <em>would</em> have been approved.
/// </param>
public sealed record RiskEvaluationResult(
    RiskDecision Decision,
    IReadOnlyList<RiskCheckOutcome> Checks,
    IReadOnlyList<string> RejectionCodes,
    PositionSizeResult? Sizing)
{
    /// <summary>Whether the trade was approved.</summary>
    public bool IsApproved => Decision == RiskDecision.Approved;

    /// <summary>Quantity cleared for execution. Zero when rejected.</summary>
    public Quantity ApprovedQuantity => IsApproved && Sizing is not null
        ? Sizing.Quantity
        : Quantity.Zero;
}
