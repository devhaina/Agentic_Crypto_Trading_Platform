using Agentiva.BuildingBlocks.TradingRules.Indicators;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Strategy;

/// <summary>
/// Tests for <see cref="IndicatorEngine"/>.
/// </summary>
/// <remarks>
/// Where a textbook reference value would need pages of worked arithmetic to
/// justify in a test's own comments, these assertions lean on constructions
/// whose correct answer is derivable by inspection instead — a flat series,
/// a monotonic one, a hand-added two-bar VWAP — so the expected value in the
/// test is itself trustworthy.
/// </remarks>
public sealed class IndicatorEngineTests
{
    [Fact]
    public void Ema_of_a_flat_series_equals_that_constant()
    {
        var closes = Enumerable.Repeat(100m, 20).ToArray();

        IndicatorEngine.Ema(closes, 12).ShouldBe(100m);
    }

    [Fact]
    public void Ema_is_null_with_fewer_bars_than_the_period()
        => IndicatorEngine.Ema([1m, 2m, 3m], 12).ShouldBeNull();

    [Fact]
    public void Rsi_is_100_for_a_strictly_increasing_series()
    {
        var closes = Enumerable.Range(0, 20).Select(i => 100m + i).ToArray();

        IndicatorEngine.Rsi(closes).ShouldBe(100m);
    }

    [Fact]
    public void Rsi_is_0_for_a_strictly_decreasing_series()
    {
        var closes = Enumerable.Range(0, 20).Select(i => 200m - i).ToArray();

        IndicatorEngine.Rsi(closes).ShouldBe(0m);
    }

    [Fact]
    public void Rsi_is_null_with_period_or_fewer_closes()
        => IndicatorEngine.Rsi(Enumerable.Repeat(1m, 14).ToArray()).ShouldBeNull();

    [Fact]
    public void Macd_of_a_flat_series_is_zero_on_every_component()
    {
        var closes = Enumerable.Repeat(100m, 40).ToArray();

        var macd = IndicatorEngine.Macd(closes);

        macd.ShouldNotBeNull();
        macd.Value.Macd.ShouldBe(0m);
        macd.Value.Signal.ShouldBe(0m);
        macd.Value.Histogram.ShouldBe(0m);
    }

    [Fact]
    public void Macd_is_null_with_too_few_closes_to_seed_the_signal_line()
        => IndicatorEngine.Macd(Enumerable.Repeat(100m, 30).ToArray()).ShouldBeNull();

    [Fact]
    public void Atr_of_a_perfectly_flat_series_is_zero()
    {
        var bars = Enumerable.Range(0, 20)
            .Select(i => Bar(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i), 100m, 100m, 100m))
            .ToArray();

        IndicatorEngine.Atr(bars).ShouldBe(0m);
    }

    [Fact]
    public void Atr_is_null_with_period_or_fewer_bars()
    {
        var bars = Enumerable.Range(0, 14)
            .Select(i => Bar(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i), 100m, 100m, 100m))
            .ToArray();

        IndicatorEngine.Atr(bars).ShouldBeNull();
    }

    [Fact]
    public void Vwap_weights_by_volume()
    {
        var open = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Typical prices 9 and 11, volumes 100 and 200: (9*100 + 11*200) / 300 = 10.3333...
        var bars = new[]
        {
            new PriceBar(open, 9m, 10m, 8m, 9m, 100m),
            new PriceBar(open.AddMinutes(1), 9m, 12m, 10m, 11m, 200m)
        };

        var vwap = IndicatorEngine.Vwap(bars);

        vwap.ShouldNotBeNull();
        Math.Round(vwap.Value, 4).ShouldBe(10.3333m);
    }

    [Fact]
    public void AnnualizedVolatilityPercent_scales_atr_by_the_square_root_of_bars_per_year()
    {
        // ATR 1% of a 100 price, on 1-day bars (365 bars/year):
        // (1/100) * sqrt(365) * 100 = sqrt(365) ~= 19.1050...
        var result = IndicatorEngine.AnnualizedVolatilityPercent(atr: 1m, price: 100m, timeframe: "1d");

        result.ShouldNotBeNull();
        Math.Round(result.Value, 2).ShouldBe(19.10m);
    }

    [Fact]
    public void AnnualizedVolatilityPercent_is_null_without_an_atr()
        => IndicatorEngine.AnnualizedVolatilityPercent(atr: null, price: 100m, timeframe: "1d").ShouldBeNull();

    [Fact]
    public void AnnualizedVolatilityPercent_is_null_for_a_non_positive_price()
        => IndicatorEngine.AnnualizedVolatilityPercent(atr: 1m, price: 0m, timeframe: "1d").ShouldBeNull();

    [Fact]
    public void AnnualizedVolatilityPercent_is_null_for_an_unrecognised_timeframe()
        => IndicatorEngine.AnnualizedVolatilityPercent(atr: 1m, price: 100m, timeframe: "7m").ShouldBeNull();

    [Fact]
    public void AnnualizedVolatilityPercent_treats_minutes_and_months_as_distinct()
    {
        // "1m" (one minute, 525,600 bars/year) and "1M" (one month, 12
        // bars/year) must not collide the way Timeframe.Create already
        // guards against for the indicator engine's own calendar table.
        var perMinute = IndicatorEngine.AnnualizedVolatilityPercent(atr: 1m, price: 100m, timeframe: "1m");
        var perMonth = IndicatorEngine.AnnualizedVolatilityPercent(atr: 1m, price: 100m, timeframe: "1M");

        perMinute.ShouldNotBeNull();
        perMonth.ShouldNotBeNull();
        perMinute.Value.ShouldNotBe(perMonth.Value);
        perMinute.Value.ShouldBeGreaterThan(perMonth.Value);
    }

    private static PriceBar Bar(DateTimeOffset openTime, decimal high, decimal low, decimal close)
        => new(openTime, close, high, low, close, 1m);
}
