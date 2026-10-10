using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Backtesting.Domain.Metrics;

/// <summary>
/// Everything one simulation run (or one walk-forward window of it) is
/// scored on.
/// </summary>
/// <remarks>
/// <see cref="TotalReturnPercent"/>, <see cref="SharpeRatio"/>,
/// <see cref="SortinoRatio"/>, <see cref="ProfitFactor"/> and the average
/// win/loss figures are plain <c>decimal</c>, not <see cref="Percentage"/> —
/// a strategy that triples capital returns 200%, and <c>Percentage</c>
/// deliberately rejects anything over 100 to catch a fraction/percent
/// mix-up elsewhere in the platform. <see cref="MaxDrawdownPercent"/> and
/// <see cref="WinRatePercent"/> are genuinely bounded to [0, 100], so they
/// keep the stronger type.
/// <para>
/// <see cref="SharpeRatio"/> and <see cref="SortinoRatio"/> are annualised
/// assuming a zero risk-free rate, and are <c>null</c> rather than an
/// artificial zero or infinity when they are not yet meaningful: fewer than
/// two bars of returns, a perfectly flat equity curve (zero volatility), or
/// — for Sortino specifically — no bar that ever lost money (zero downside
/// deviation). <see cref="ProfitFactor"/> is <c>null</c> for the same
/// reason when there have been no losing trades yet.
/// </para>
/// </remarks>
public sealed record PerformanceMetrics(
    decimal StartingEquity,
    decimal FinalEquity,
    decimal TotalReturnPercent,
    decimal? SharpeRatio,
    decimal? SortinoRatio,
    decimal? ProfitFactor,
    Percentage MaxDrawdownPercent,
    Percentage WinRatePercent,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal AverageWinPercent,
    decimal AverageLossPercent);
