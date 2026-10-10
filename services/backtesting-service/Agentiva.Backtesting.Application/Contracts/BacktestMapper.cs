using Agentiva.Backtesting.Domain.Metrics;
using Agentiva.Backtesting.Domain.Runs;

namespace Agentiva.Backtesting.Application.Contracts;

/// <summary>Maps the domain's own types onto the DTOs the HTTP surface returns.</summary>
internal static class BacktestMapper
{
    public static BacktestResultDto ToResultDto(BacktestRun run) => new(
        run.Id.Value, run.Symbol.Value, run.Timeframe, run.StrategyName, run.StrategyVersion,
        run.PeriodStart, run.PeriodEnd, run.StartingCapital, run.FeePercent.Percent, run.SlippagePercent.Percent,
        run.Status.ToString(), run.CreatedAt, run.CompletedAt, run.FailureReason,
        run.Metrics is null ? null : ToMetricsDto(run.Metrics),
        run.WalkForwardWindows.Select(ToWindowDto).ToArray(), run.Trades.Count);

    public static BacktestSummaryDto ToSummaryDto(BacktestRun run) => new(
        run.Id.Value, run.Symbol.Value, run.Timeframe, run.StrategyName, run.StrategyVersion,
        run.PeriodStart, run.PeriodEnd, run.Status.ToString(), run.CreatedAt, run.CompletedAt,
        run.Metrics?.TotalReturnPercent, run.Trades.Count);

    public static BacktestTradeDto ToTradeDto(BacktestTrade trade) => new(
        trade.Id.Value, trade.Side.ToString(), trade.EntryTime, trade.EntryPrice, trade.Quantity,
        trade.ExitTime, trade.ExitPrice, trade.ExitReason.ToString(), trade.GrossPnl, trade.Fees,
        trade.SlippageCost, trade.NetPnl, trade.ReasonCodes);

    private static PerformanceMetricsDto ToMetricsDto(PerformanceMetrics metrics) => new(
        metrics.StartingEquity, metrics.FinalEquity, metrics.TotalReturnPercent, metrics.SharpeRatio,
        metrics.SortinoRatio, metrics.ProfitFactor, metrics.MaxDrawdownPercent.Percent, metrics.WinRatePercent.Percent,
        metrics.TotalTrades, metrics.WinningTrades, metrics.LosingTrades, metrics.AverageWinPercent,
        metrics.AverageLossPercent);

    private static WalkForwardWindowDto ToWindowDto(WalkForwardWindow window) => new(
        window.WindowIndex, window.PeriodStart, window.PeriodEnd, ToMetricsDto(window.Metrics));
}
