using Agentiva.Backtesting.Domain.Runs;
using Agentiva.Backtesting.Domain.Simulation;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.TradingRules.Indicators;
using Agentiva.BuildingBlocks.TradingRules.Strategies;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Backtesting;

/// <summary>
/// Tests for <see cref="BacktestSimulator"/> against a trivial strategy
/// double rather than the real EMA/RSI/Breakout/Trend-Following rules, so
/// these assert the simulator's own mechanics — entry timing, stop/take
/// detection, fees, slippage, look-ahead safety — independent of any one
/// strategy's indicator math.
/// </summary>
public sealed class BacktestSimulatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly BacktestId RunId = BacktestId.New();

    [Fact]
    public void Run_opens_no_position_before_the_strategy_has_enough_bars()
    {
        var strategy = new BuysOnceReadyStrategy(minimumBars: 5, entryPrice: 100m, stop: 90m, take: 120m);
        var bars = FlatBars(count: 3, price: 100m);

        var result = Run(strategy, bars);

        result.Trades.ShouldBeEmpty();
    }

    [Fact]
    public void Run_does_not_check_stop_or_take_against_the_bar_that_opened_the_position()
    {
        // The strategy's own signal bar has a Low of 80, below the 90 stop it
        // sets for itself. If the simulator checked the entry bar's own range
        // against its own freshly-set stop, it would close the trade
        // immediately on the bar that opened it — exactly the look-ahead bug
        // this test exists to catch. nextBar's range (95-105) touches neither
        // the stop nor the take, so the only way this position closes at all
        // is the end-of-data sweep on nextBar itself.
        var strategy = new BuysOnceReadyStrategy(minimumBars: 1, entryPrice: 100m, stop: 90m, take: 120m);

        var signalBar = new PriceBar(Start, 100m, 105m, 80m, 100m, 1m);
        var nextBar = new PriceBar(Start.AddMinutes(1), 100m, 105m, 95m, 100m, 1m);

        var result = Run(strategy, [signalBar, nextBar]);

        result.Trades.Count.ShouldBe(1);
        result.Trades[0].ExitReason.ShouldBe(ExitReason.EndOfData);
    }

    [Fact]
    public void Run_closes_at_the_stop_when_a_later_bars_low_touches_it()
    {
        var strategy = new BuysOnceReadyStrategy(minimumBars: 1, entryPrice: 100m, stop: 90m, take: 120m);

        var signalBar = new PriceBar(Start, 100m, 100m, 100m, 100m, 1m);
        var stopBar = new PriceBar(Start.AddMinutes(1), 95m, 96m, 90m, 94m, 1m);

        var result = Run(strategy, [signalBar, stopBar]);

        result.Trades.Count.ShouldBe(1);
        result.Trades[0].ExitReason.ShouldBe(ExitReason.StopLoss);
        result.Trades[0].ExitPrice.ShouldBe(90m);
    }

    [Fact]
    public void Run_closes_at_the_take_profit_when_a_later_bars_high_touches_it()
    {
        var strategy = new BuysOnceReadyStrategy(minimumBars: 1, entryPrice: 100m, stop: 90m, take: 120m);

        var signalBar = new PriceBar(Start, 100m, 100m, 100m, 100m, 1m);
        var takeBar = new PriceBar(Start.AddMinutes(1), 110m, 120m, 108m, 115m, 1m);

        var result = Run(strategy, [signalBar, takeBar]);

        result.Trades.Count.ShouldBe(1);
        result.Trades[0].ExitReason.ShouldBe(ExitReason.TakeProfit);
        result.Trades[0].ExitPrice.ShouldBe(120m);
    }

    [Fact]
    public void Run_assumes_the_stop_was_hit_first_when_one_bar_touches_both()
    {
        var strategy = new BuysOnceReadyStrategy(minimumBars: 1, entryPrice: 100m, stop: 90m, take: 120m);

        var signalBar = new PriceBar(Start, 100m, 100m, 100m, 100m, 1m);
        var bothBar = new PriceBar(Start.AddMinutes(1), 100m, 125m, 85m, 100m, 1m);

        var result = Run(strategy, [signalBar, bothBar]);

        result.Trades.Count.ShouldBe(1);
        result.Trades[0].ExitReason.ShouldBe(ExitReason.StopLoss);
    }

    [Fact]
    public void Run_fills_at_the_worse_of_the_open_or_the_level_on_a_gap()
    {
        // The bar gapped open below the 90 stop entirely — a real fill could
        // never have happened at exactly 90.
        var strategy = new BuysOnceReadyStrategy(minimumBars: 1, entryPrice: 100m, stop: 90m, take: 120m);

        var signalBar = new PriceBar(Start, 100m, 100m, 100m, 100m, 1m);
        var gapBar = new PriceBar(Start.AddMinutes(1), 80m, 82m, 78m, 81m, 1m);

        var result = Run(strategy, [signalBar, gapBar]);

        result.Trades[0].ExitPrice.ShouldBe(80m);
    }

    [Fact]
    public void Run_closes_a_still_open_position_at_the_final_bar_as_end_of_data()
    {
        var strategy = new BuysOnceReadyStrategy(minimumBars: 1, entryPrice: 100m, stop: 50m, take: 200m);

        var signalBar = new PriceBar(Start, 100m, 100m, 100m, 100m, 1m);
        var finalBar = new PriceBar(Start.AddMinutes(1), 105m, 108m, 103m, 106m, 1m);

        var result = Run(strategy, [signalBar, finalBar]);

        result.Trades.Count.ShouldBe(1);
        result.Trades[0].ExitReason.ShouldBe(ExitReason.EndOfData);
        result.Trades[0].ExitPrice.ShouldBe(106m);
    }

    [Fact]
    public void Run_deducts_fees_and_slippage_from_net_pnl_but_not_from_gross_pnl()
    {
        var strategy = new BuysOnceReadyStrategy(minimumBars: 1, entryPrice: 100m, stop: 50m, take: 200m);

        var signalBar = new PriceBar(Start, 100m, 100m, 100m, 100m, 1m);
        var winBar = new PriceBar(Start.AddMinutes(1), 110m, 120m, 108m, 115m, 1m);

        var output = BacktestSimulator.Run(
            strategy, [signalBar, winBar], RunId, "BTCUSDT", "1m",
            candleBufferCapacity: 250, startingCapital: 10_000m,
            feePercent: Percentage.FromPercent(1m), slippagePercent: Percentage.FromPercent(0.5m),
            riskPerTradePercent: Percentage.FromPercent(100m));

        var trade = output.Trades[0];

        trade.GrossPnl.ShouldBeGreaterThan(0m);
        trade.Fees.ShouldBeGreaterThan(0m);
        trade.SlippageCost.ShouldBeGreaterThan(0m);
        trade.NetPnl.ShouldBe(trade.GrossPnl - trade.Fees - trade.SlippageCost);
    }

    [Fact]
    public void Run_never_opens_a_new_position_on_the_same_bar_one_just_closed()
    {
        // A strategy that signals Buy on every eligible bar. If the simulator
        // allowed a same-bar re-entry, the second trade would be entered on
        // the stop bar itself rather than the bar after it.
        var strategy = new BuysOnceReadyStrategy(minimumBars: 1, entryPrice: 100m, stop: 90m, take: 120m);

        var signalBar = new PriceBar(Start, 100m, 100m, 100m, 100m, 1m);
        var stopBar = new PriceBar(Start.AddMinutes(1), 95m, 96m, 90m, 94m, 1m);
        var nextBar = new PriceBar(Start.AddMinutes(2), 94m, 96m, 93m, 95m, 1m);

        var result = Run(strategy, [signalBar, stopBar, nextBar]);

        // The first trade closes on stopBar; the strategy is eligible to buy
        // again immediately, but the earliest that can land is nextBar — and
        // since nextBar is also the last bar, that second position is closed
        // by the end-of-data sweep, on the same bar it opened.
        result.Trades.Count.ShouldBe(2);
        result.Trades[0].EntryTime.ShouldBe(signalBar.OpenTime);
        result.Trades[0].ExitTime.ShouldBe(stopBar.OpenTime);
        result.Trades[1].EntryTime.ShouldBe(nextBar.OpenTime);
    }

    private static SimulationOutput Run(IStrategy strategy, IReadOnlyList<PriceBar> bars)
        => BacktestSimulator.Run(
            strategy, bars, RunId, "BTCUSDT", "1m",
            candleBufferCapacity: 250, startingCapital: 10_000m,
            feePercent: Percentage.Zero, slippagePercent: Percentage.Zero,
            riskPerTradePercent: Percentage.FromPercent(100m));

    private static PriceBar[] FlatBars(int count, decimal price)
        => Enumerable.Range(0, count)
            .Select(i => new PriceBar(Start.AddMinutes(i), price, price, price, price, 1m))
            .ToArray();

    /// <summary>Buys exactly once, at a fixed price/stop/take, as soon as it has enough bars.</summary>
    private sealed class BuysOnceReadyStrategy(int minimumBars, decimal entryPrice, decimal stop, decimal take)
        : IStrategy
    {
        public string Name => "TEST_BUY_ONCE_READY";

        public string Version => "1.0.0";

        public int MinimumBars => minimumBars;

        public StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
        {
            if (context.Bars.Count < MinimumBars)
            {
                return StrategyEvaluationResult.Hold(entryPrice, "INSUFFICIENT_DATA");
            }

            return new StrategyEvaluationResult(TradeAction.Buy, Percentage.FromPercent(60m), entryPrice, stop, take, ["TEST_SIGNAL"]);
        }
    }
}
