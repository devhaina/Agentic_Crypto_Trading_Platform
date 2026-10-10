using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.TradingRules.Indicators;
using Agentiva.Backtesting.Domain.Runs;
using Agentiva.Backtesting.Domain.Simulation;

namespace Agentiva.Backtesting.Domain.Metrics;

/// <summary>
/// Scores one simulation's trades and equity curve. Every function here is
/// pure arithmetic over data the simulator already produced — nothing here
/// re-runs a strategy or touches historical data.
/// </summary>
public static class PerformanceCalculator
{
    /// <summary>Computes every metric over one trade list and equity curve.</summary>
    public static PerformanceMetrics Calculate(
        IReadOnlyList<BacktestTrade> trades,
        IReadOnlyList<EquityPoint> equityCurve,
        decimal startingEquity,
        string timeframe)
    {
        var finalEquity = equityCurve.Count > 0 ? equityCurve[^1].Equity : startingEquity;
        var totalReturnPercent = startingEquity > 0m
            ? (finalEquity - startingEquity) / startingEquity * 100m
            : 0m;

        var winners = trades.Where(t => t.NetPnl > 0m).ToArray();
        var losers = trades.Where(t => t.NetPnl < 0m).ToArray();

        var winRate = trades.Count > 0
            ? Percentage.FromFraction(Math.Clamp((decimal)winners.Length / trades.Count, 0m, 1m))
            : Percentage.Zero;

        var grossProfit = winners.Sum(t => t.NetPnl);
        var grossLoss = losers.Sum(t => t.NetPnl);

        // Undefined, not infinite, until a losing trade actually happens —
        // the same reasoning IndicatorEngine applies to a too-short series:
        // an honest "not yet meaningful" beats a number that only looks real.
        decimal? profitFactor = grossLoss < 0m ? grossProfit / Math.Abs(grossLoss) : null;

        var averageWinPercent = winners.Length > 0 ? winners.Average(PercentReturnOnRisk) : 0m;
        var averageLossPercent = losers.Length > 0 ? losers.Average(PercentReturnOnRisk) : 0m;

        var maxDrawdown = MaxDrawdownPercent(equityCurve, startingEquity);

        var barReturns = BarReturns(equityCurve, startingEquity);
        var barsPerYear = TimeframeCalendar.BarsPerYear(timeframe);

        return new PerformanceMetrics(
            startingEquity, finalEquity, totalReturnPercent,
            Sharpe(barReturns, barsPerYear), Sortino(barReturns, barsPerYear), profitFactor,
            maxDrawdown, winRate, trades.Count, winners.Length, losers.Length,
            averageWinPercent, averageLossPercent);
    }

    /// <summary>
    /// Slices the full run into <paramref name="windowCount"/> sequential,
    /// non-overlapping calendar windows and scores each independently — see
    /// the remarks on <see cref="WalkForwardWindow"/> for what this does and
    /// does not validate.
    /// </summary>
    public static IReadOnlyList<WalkForwardWindow> SplitIntoWindows(
        IReadOnlyList<BacktestTrade> trades,
        IReadOnlyList<EquityPoint> equityCurve,
        decimal startingEquity,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        int windowCount,
        string timeframe)
    {
        if (windowCount <= 1)
        {
            return [];
        }

        var windowLength = (periodEnd - periodStart) / windowCount;
        var windows = new List<WalkForwardWindow>(windowCount);
        var carryEquity = startingEquity;

        for (var i = 0; i < windowCount; i++)
        {
            var windowStart = periodStart + windowLength * i;
            var windowEnd = i == windowCount - 1 ? periodEnd : windowStart + windowLength;

            var windowEquityCurve = equityCurve
                .Where(p => p.Time >= windowStart && p.Time < windowEnd)
                .ToArray();

            // A trade is attributed to the window it entered in, even if it
            // closed slightly after the boundary — the same approximation
            // the overall run already makes at its own edges, and the
            // simplest honest choice given trades are not sliced mid-flight.
            var windowTrades = trades
                .Where(t => t.EntryTime >= windowStart && t.EntryTime < windowEnd)
                .ToArray();

            var metrics = Calculate(windowTrades, windowEquityCurve, carryEquity, timeframe);
            windows.Add(new WalkForwardWindow(i, windowStart, windowEnd, metrics));

            if (windowEquityCurve.Length > 0)
            {
                carryEquity = windowEquityCurve[^1].Equity;
            }
        }

        return windows;
    }

    /// <summary>Net P&amp;L relative to the notional actually at risk in that trade.</summary>
    private static decimal PercentReturnOnRisk(BacktestTrade trade)
    {
        var notional = trade.EntryPrice * trade.Quantity;
        return notional > 0m ? trade.NetPnl / notional * 100m : 0m;
    }

    /// <summary>Peak-to-trough drawdown against every equity value this run ever recorded.</summary>
    private static Percentage MaxDrawdownPercent(IReadOnlyList<EquityPoint> equityCurve, decimal startingEquity)
    {
        var peak = startingEquity;
        var maxDrawdownFraction = 0m;

        foreach (var point in equityCurve)
        {
            if (point.Equity > peak)
            {
                peak = point.Equity;
                continue;
            }

            if (peak <= 0m)
            {
                continue;
            }

            var drawdown = (peak - point.Equity) / peak;
            if (drawdown > maxDrawdownFraction)
            {
                maxDrawdownFraction = drawdown;
            }
        }

        return Percentage.FromFraction(Math.Clamp(maxDrawdownFraction, 0m, 1m));
    }

    /// <summary>Per-bar fractional returns, starting from <paramref name="startingEquity"/>.</summary>
    private static decimal[] BarReturns(IReadOnlyList<EquityPoint> equityCurve, decimal startingEquity)
    {
        var previous = startingEquity;
        var returns = new List<decimal>(equityCurve.Count);

        foreach (var point in equityCurve)
        {
            if (previous > 0m)
            {
                returns.Add((point.Equity - previous) / previous);
            }

            previous = point.Equity;
        }

        return returns.ToArray();
    }

    /// <summary>
    /// Annualised Sharpe ratio, assuming a zero risk-free rate. <c>null</c>
    /// when there are too few returns, every return was identical, or the
    /// timeframe's bars-per-year is not recognised.
    /// </summary>
    private static decimal? Sharpe(decimal[] returns, int? barsPerYear)
    {
        if (returns.Length < 2 || barsPerYear is null)
        {
            return null;
        }

        var mean = returns.Average();
        var variance = returns.Average(r => (r - mean) * (r - mean));
        var stdev = (decimal)Math.Sqrt((double)variance);

        if (stdev == 0m)
        {
            return null;
        }

        return mean / stdev * (decimal)Math.Sqrt(barsPerYear.Value);
    }

    /// <summary>
    /// Annualised Sortino ratio: like <see cref="Sharpe"/> but the
    /// denominator only penalises downside returns. <c>null</c> under the
    /// same conditions as <see cref="Sharpe"/>, or when no bar ever lost
    /// money — the downside deviation is then zero and the ratio undefined,
    /// not infinite.
    /// </summary>
    private static decimal? Sortino(decimal[] returns, int? barsPerYear)
    {
        if (returns.Length < 2 || barsPerYear is null)
        {
            return null;
        }

        var mean = returns.Average();
        var downsideVariance = returns.Average(r => r < 0m ? r * r : 0m);
        var downsideDeviation = (decimal)Math.Sqrt((double)downsideVariance);

        if (downsideDeviation == 0m)
        {
            return null;
        }

        return mean / downsideDeviation * (decimal)Math.Sqrt(barsPerYear.Value);
    }
}
