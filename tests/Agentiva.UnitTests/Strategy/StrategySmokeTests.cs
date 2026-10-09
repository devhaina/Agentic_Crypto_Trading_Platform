using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Strategy.Domain.Indicators;
using Agentiva.Strategy.Domain.Strategies;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Strategy;

/// <summary>
/// Smoke tests for the three strategies: each produces the expected action
/// on an unambiguous synthetic series, and holds for insufficient data.
/// </summary>
/// <remarks>
/// These are deliberately not exhaustive decision-table tests — the synthetic
/// series here are far cleaner trends and breakouts than real market data
/// ever is. Their job is to catch a strategy wired backwards (buying a
/// downtrend) or a lookback miscounted by one, not to validate trading
/// performance.
/// </remarks>
public sealed class StrategySmokeTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmaRsi_holds_with_insufficient_data()
    {
        var bars = Series(count: 10, priceAt: i => 100m + i);
        var result = new EmaRsiStrategy().Evaluate(Context(bars));

        result.Action.ShouldBe(TradeAction.Hold);
        result.ReasonCodes.ShouldContain("INSUFFICIENT_DATA");
    }

    [Fact]
    public void EmaRsi_buys_somewhere_during_a_sustained_upward_move_after_a_noisy_flat_run()
    {
        // A two-sided wobble for long enough to seed both EMAs without
        // pinning RSI at an extreme (a perfectly flat run has zero losses,
        // and Wilder's RSI is then undefined-high the instant any gain
        // appears — correct arithmetic, but not a fixture this test should
        // depend on), then a sustained climb. The strategy is evaluated on
        // every growing prefix the way the real ingestion pipeline evaluates
        // it on every new candle, rather than asserting the cross lands on
        // one specific, hand-counted bar.
        var bars = Series(count: 60, priceAt: i => i < 30
            ? 100m + 2m * (decimal)Math.Sin(i)
            : 100m + (i - 29) * 0.8m + 0.3m * (decimal)Math.Sin(i - 29));

        var strategy = new EmaRsiStrategy();
        var buys = Enumerable.Range(strategy.MinimumBars, bars.Length - strategy.MinimumBars + 1)
            .Select(count => strategy.Evaluate(Context(bars[..count])))
            .Where(r => r.Action == TradeAction.Buy)
            .ToArray();

        buys.ShouldNotBeEmpty();
        buys.ShouldAllBe(r => r.ReasonCodes.Contains("EMA_CROSS_UP"));
        buys.ShouldAllBe(r => r.StopLoss < r.EntryPrice && r.TakeProfit > r.EntryPrice);
    }

    [Fact]
    public void TrendFollowing_holds_with_insufficient_data()
    {
        var bars = Series(count: 50, priceAt: i => 100m + i);
        var result = new TrendFollowingStrategy().Evaluate(Context(bars));

        result.Action.ShouldBe(TradeAction.Hold);
        result.ReasonCodes.ShouldContain("INSUFFICIENT_DATA");
    }

    [Fact]
    public void TrendFollowing_buys_a_sustained_uptrend()
    {
        // A steady climb with a gentle oscillation riding on top — same
        // reasoning as the EMA/RSI test above: a perfectly monotonic 250-bar
        // climb pins RSI at 100 and trips this strategy's own extreme-RSI
        // filter before the trend condition is even the interesting part of
        // the test.
        var bars = Series(count: 250, priceAt: i => 100m + i * 0.3m + 3m * (decimal)Math.Sin(i * 0.3));

        var result = new TrendFollowingStrategy().Evaluate(Context(bars));

        result.Action.ShouldBe(TradeAction.Buy);
        result.ReasonCodes.ShouldContain("UPTREND_EMA50_ABOVE_EMA200");
    }

    [Fact]
    public void TrendFollowing_sells_a_sustained_downtrend()
    {
        var bars = Series(count: 250, priceAt: i => 1000m - i * 0.3m + 3m * (decimal)Math.Sin(i * 0.3));

        var result = new TrendFollowingStrategy().Evaluate(Context(bars));

        result.Action.ShouldBe(TradeAction.Sell);
        result.ReasonCodes.ShouldContain("DOWNTREND_EMA50_BELOW_EMA200");
    }

    [Fact]
    public void Breakout_holds_with_insufficient_data()
    {
        var bars = Series(count: 10, priceAt: i => 100m + i);
        var result = new BreakoutStrategy().Evaluate(Context(bars));

        result.Action.ShouldBe(TradeAction.Hold);
        result.ReasonCodes.ShouldContain("INSUFFICIENT_DATA");
    }

    [Fact]
    public void Breakout_buys_a_close_above_the_prior_channel_high()
    {
        // 25 flat bars at 100 set the channel, then one bar closes well above
        // every high the channel has ever seen.
        var flat = Series(count: 25, priceAt: _ => 100m);
        var breakoutBar = new PriceBar(Start.AddMinutes(25), 100m, 110m, 100m, 110m, 1m);
        var bars = flat.Append(breakoutBar).ToArray();

        var result = new BreakoutStrategy().Evaluate(Context(bars));

        result.Action.ShouldBe(TradeAction.Buy);
        result.ReasonCodes.ShouldContain("BREAKOUT_ABOVE_DONCHIAN_HIGH");
    }

    [Fact]
    public void Breakout_holds_while_price_stays_within_the_channel()
    {
        var bars = Series(count: 30, priceAt: _ => 100m);

        var result = new BreakoutStrategy().Evaluate(Context(bars));

        result.Action.ShouldBe(TradeAction.Hold);
        result.ReasonCodes.ShouldContain("WITHIN_CHANNEL");
    }

    private static PriceBar[] Series(int count, Func<int, decimal> priceAt)
        => Enumerable.Range(0, count)
            .Select(i =>
            {
                var price = priceAt(i);
                return new PriceBar(Start.AddMinutes(i), price, price + 0.1m, price - 0.1m, price, 1m);
            })
            .ToArray();

    private static StrategyEvaluationContext Context(IReadOnlyList<PriceBar> bars)
        => new("BTCUSDT", "1m", bars, IndicatorSet.Compute(bars));
}
