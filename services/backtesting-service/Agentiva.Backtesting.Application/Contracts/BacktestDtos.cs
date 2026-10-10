namespace Agentiva.Backtesting.Application.Contracts;

public sealed record BacktestTradeDto(
    Guid BacktestTradeId,
    string Side,
    DateTimeOffset EntryTime,
    decimal EntryPrice,
    decimal Quantity,
    DateTimeOffset ExitTime,
    decimal ExitPrice,
    string ExitReason,
    decimal GrossPnl,
    decimal Fees,
    decimal SlippageCost,
    decimal NetPnl,
    IReadOnlyList<string> ReasonCodes);

public sealed record PerformanceMetricsDto(
    decimal StartingEquity,
    decimal FinalEquity,
    decimal TotalReturnPercent,
    decimal? SharpeRatio,
    decimal? SortinoRatio,
    decimal? ProfitFactor,
    decimal MaxDrawdownPercent,
    decimal WinRatePercent,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal AverageWinPercent,
    decimal AverageLossPercent);

public sealed record WalkForwardWindowDto(
    int WindowIndex,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    PerformanceMetricsDto Metrics);

/// <summary>One row of <c>GET /api/v1/backtests</c> — enough to list without fetching every trade.</summary>
public sealed record BacktestSummaryDto(
    Guid BacktestId,
    string Symbol,
    string Timeframe,
    string StrategyName,
    string StrategyVersion,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    decimal? TotalReturnPercent,
    int TradeCount);

/// <summary>The full result of one backtest, minus its individual trades — see <c>GetBacktestTradesQuery</c>.</summary>
public sealed record BacktestResultDto(
    Guid BacktestId,
    string Symbol,
    string Timeframe,
    string StrategyName,
    string StrategyVersion,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal StartingCapital,
    decimal FeePercent,
    decimal SlippagePercent,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    PerformanceMetricsDto? Metrics,
    IReadOnlyList<WalkForwardWindowDto> WalkForwardWindows,
    int TradeCount);
