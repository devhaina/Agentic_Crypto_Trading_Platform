using Agentiva.Backtesting.Domain.Metrics;
using Agentiva.Backtesting.Domain.Runs;
using Agentiva.Backtesting.Domain.Simulation;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Backtesting;

/// <summary>
/// Tests for <see cref="PerformanceCalculator"/>, using equity curves and
/// trade lists whose correct answer is derivable by inspection rather than
/// a worked textbook example.
/// </summary>
public sealed class PerformanceCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly BacktestId RunId = BacktestId.New();

    [Fact]
    public void Calculate_with_no_trades_and_flat_equity_is_all_zero_or_null()
    {
        var equityCurve = Points([100m, 100m, 100m]);

        var metrics = PerformanceCalculator.Calculate([], equityCurve, startingEquity: 100m, timeframe: "1d");

        metrics.TotalReturnPercent.ShouldBe(0m);
        metrics.ProfitFactor.ShouldBeNull();
        metrics.SharpeRatio.ShouldBeNull();
        metrics.SortinoRatio.ShouldBeNull();
        metrics.WinRatePercent.Percent.ShouldBe(0m);
        metrics.MaxDrawdownPercent.Percent.ShouldBe(0m);
        metrics.TotalTrades.ShouldBe(0);
    }

    [Fact]
    public void Calculate_total_return_is_final_over_starting_equity()
    {
        var equityCurve = Points([110m, 125m, 150m]);

        var metrics = PerformanceCalculator.Calculate([], equityCurve, startingEquity: 100m, timeframe: "1d");

        metrics.FinalEquity.ShouldBe(150m);
        metrics.TotalReturnPercent.ShouldBe(50m);
    }

    [Fact]
    public void Calculate_profit_factor_is_null_with_no_losing_trades()
    {
        var trades = new[] { Trade(netPnl: 100m), Trade(netPnl: 50m) };

        var metrics = PerformanceCalculator.Calculate(trades, Points([100m]), startingEquity: 100m, timeframe: "1d");

        metrics.ProfitFactor.ShouldBeNull();
    }

    [Fact]
    public void Calculate_profit_factor_is_gross_profit_over_gross_loss()
    {
        var trades = new[] { Trade(netPnl: 300m), Trade(netPnl: -100m), Trade(netPnl: -50m) };

        var metrics = PerformanceCalculator.Calculate(trades, Points([100m]), startingEquity: 100m, timeframe: "1d");

        // 300 / (100 + 50) = 2.
        metrics.ProfitFactor.ShouldBe(2m);
    }

    [Fact]
    public void Calculate_win_rate_is_winning_trades_over_total()
    {
        var trades = new[] { Trade(10m), Trade(10m), Trade(-5m), Trade(-5m) };

        var metrics = PerformanceCalculator.Calculate(trades, Points([100m]), startingEquity: 100m, timeframe: "1d");

        metrics.WinRatePercent.Percent.ShouldBe(50m);
        metrics.WinningTrades.ShouldBe(2);
        metrics.LosingTrades.ShouldBe(2);
        metrics.TotalTrades.ShouldBe(4);
    }

    [Fact]
    public void Calculate_max_drawdown_is_the_single_largest_peak_to_trough_drop()
    {
        // Peak 150, trough 90: (150-90)/150 = 40%. A later smaller dip from
        // 120 to 100 (16.7%) must not override the larger one already found.
        var equityCurve = Points([100m, 150m, 90m, 120m, 100m]);

        var metrics = PerformanceCalculator.Calculate([], equityCurve, startingEquity: 100m, timeframe: "1d");

        metrics.MaxDrawdownPercent.Percent.ShouldBe(40m);
    }

    [Fact]
    public void Calculate_sharpe_is_null_for_a_perfectly_flat_equity_curve()
    {
        // Zero volatility: the standard deviation of returns is zero, so the
        // ratio is undefined rather than reported as an artificial infinity.
        var equityCurve = Points([100m, 100m, 100m, 100m]);

        var metrics = PerformanceCalculator.Calculate([], equityCurve, startingEquity: 100m, timeframe: "1d");

        metrics.SharpeRatio.ShouldBeNull();
        metrics.SortinoRatio.ShouldBeNull();
    }

    [Fact]
    public void Calculate_sortino_is_null_when_no_bar_ever_lost_money()
    {
        // Every bar gains, so downside deviation is zero even though
        // volatility (and therefore Sharpe) is not.
        var equityCurve = Points([101m, 103m, 105m, 110m]);

        var metrics = PerformanceCalculator.Calculate([], equityCurve, startingEquity: 100m, timeframe: "1d");

        metrics.SharpeRatio.ShouldNotBeNull();
        metrics.SortinoRatio.ShouldBeNull();
    }

    [Fact]
    public void Calculate_ratios_are_null_for_an_unrecognised_timeframe()
    {
        var equityCurve = Points([90m, 110m, 95m, 115m]);

        var metrics = PerformanceCalculator.Calculate([], equityCurve, startingEquity: 100m, timeframe: "not-a-timeframe");

        metrics.SharpeRatio.ShouldBeNull();
        metrics.SortinoRatio.ShouldBeNull();
    }

    [Fact]
    public void SplitIntoWindows_returns_nothing_for_a_single_window()
        => PerformanceCalculator
            .SplitIntoWindows([], Points([100m]), 100m, Start, Start.AddDays(10), windowCount: 1, timeframe: "1d")
            .ShouldBeEmpty();

    [Fact]
    public void SplitIntoWindows_splits_the_full_period_into_the_requested_count()
    {
        var equityCurve = new[]
        {
            new EquityPoint(Start.AddDays(1), 105m),
            new EquityPoint(Start.AddDays(6), 95m),
            new EquityPoint(Start.AddDays(11), 120m)
        };

        var windows = PerformanceCalculator.SplitIntoWindows(
            [], equityCurve, startingEquity: 100m, Start, Start.AddDays(12), windowCount: 3, timeframe: "1d");

        windows.Count.ShouldBe(3);
        windows[0].PeriodStart.ShouldBe(Start);
        windows[0].PeriodEnd.ShouldBe(Start.AddDays(4));
        windows[1].PeriodStart.ShouldBe(Start.AddDays(4));
        windows[2].PeriodEnd.ShouldBe(Start.AddDays(12));
    }

    [Fact]
    public void SplitIntoWindows_carries_equity_forward_between_windows()
    {
        // Window 1 ends at 150 (a 50% gain on its own starting 100); window 2
        // must be scored against 150 as ITS starting equity, not the overall
        // run's original 100 — otherwise its own return figure would be
        // wrong by exactly the first window's gain.
        var equityCurve = new[]
        {
            new EquityPoint(Start.AddDays(2), 150m),
            new EquityPoint(Start.AddDays(7), 180m)
        };

        var windows = PerformanceCalculator.SplitIntoWindows(
            [], equityCurve, startingEquity: 100m, Start, Start.AddDays(10), windowCount: 2, timeframe: "1d");

        windows[1].Metrics.StartingEquity.ShouldBe(150m);
        windows[1].Metrics.TotalReturnPercent.ShouldBe(20m);
    }

    private static EquityPoint[] Points(decimal[] values)
        => values
            .Select((value, index) => new EquityPoint(Start.AddDays(index), value))
            .ToArray();

    /// <summary>
    /// A trade with an exact, hand-chosen net P&amp;L: entry/exit prices and
    /// quantity are fixtures only, picked so (exit - entry) * quantity ==
    /// <paramref name="netPnl"/> with zero fees and slippage, since these
    /// tests are about how Calculate aggregates already-realised results,
    /// not the simulator's own cost model.
    /// </summary>
    private static BacktestTrade Trade(decimal netPnl)
        => BacktestTrade.Record(
            RunId, TradeAction.Buy, Start, entryPrice: 100m, quantity: 1m,
            Start.AddHours(1), exitPrice: 100m + netPnl, ExitReason.EndOfData,
            grossPnl: netPnl, fees: 0m, slippageCost: 0m, reasonCodes: []);
}
