namespace Agentiva.Strategy.Application.Contracts;

/// <summary>A recorded signal, as returned by the signals endpoint.</summary>
public sealed record SignalDto(
    Guid Id,
    Guid StrategyId,
    string StrategyName,
    string StrategyVersion,
    string Symbol,
    string Timeframe,
    string Action,
    decimal Confidence,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    IReadOnlyList<string> ReasonCodes,
    DateTimeOffset ComputedAt);

/// <summary>
/// Descriptive counts for one strategy. Deliberately not a win rate, a
/// Sharpe ratio or any other outcome-based metric: those require knowing
/// what actually happened to a signal after it was produced, which needs a
/// filled order (Phase 5) and a realised P&amp;L (Phase 6/8). Reporting one
/// here would be either fabricated or silently wrong — see
/// docs/architecture/known-limitations.md.
/// </summary>
public sealed record StrategyPerformanceDto(
    Guid StrategyId,
    string StrategyName,
    string StrategyVersion,
    bool IsActive,
    long TotalSignals,
    long BuySignals,
    long SellSignals,
    DateTimeOffset? FirstSignalAt,
    DateTimeOffset? LastSignalAt);
