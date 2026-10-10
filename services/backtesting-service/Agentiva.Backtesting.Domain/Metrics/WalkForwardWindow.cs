namespace Agentiva.Backtesting.Domain.Metrics;

/// <summary>
/// One sequential, non-overlapping slice of the full backtest period, scored
/// independently.
/// </summary>
/// <remarks>
/// A textbook walk-forward validation re-fits a strategy's tunable
/// parameters on each window's in-sample data before scoring it out-of-sample.
/// The three strategies this service replays (<c>EMA_RSI</c>,
/// <c>BREAKOUT</c>, <c>TREND_FOLLOWING</c>) expose no tunable parameters —
/// their periods are fixed constants in code, deliberately, so a live signal
/// is reproducible — so there is nothing to re-fit. What this still checks,
/// honestly: whether the strategy's performance is consistent across time or
/// concentrated in one lucky window, which is the failure walk-forward
/// validation exists to catch even without a parameter search. See
/// docs/architecture/known-limitations.md.
/// </remarks>
public sealed record WalkForwardWindow(
    int WindowIndex,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    PerformanceMetrics Metrics);
