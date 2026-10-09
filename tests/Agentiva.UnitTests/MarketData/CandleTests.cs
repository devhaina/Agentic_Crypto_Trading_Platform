using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.MarketData.Domain.Entities;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.MarketData;

/// <summary>
/// Tests for <see cref="Candle"/>'s OHLC invariant.
/// </summary>
/// <remarks>
/// Mirrors the <c>ck_market_candles_ohlc</c> constraint on <c>market_candles</c>
/// exactly, so that a malformed exchange payload is rejected here rather than
/// by a database round trip.
/// </remarks>
public sealed class CandleTests
{
    private static readonly DateTimeOffset OpenTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CloseTime = OpenTime.AddMinutes(15);

    [Fact]
    public void Accepts_an_internally_consistent_range()
    {
        var candle = Candle.Create(
            "BTCUSDT", "15m", OpenTime, CloseTime,
            open: 100m, high: 110m, low: 95m, close: 105m,
            volume: 10m, quoteVolume: 1_000m, tradeCount: 50, exchange: "binance");

        candle.Open.Value.ShouldBe(100m);
        candle.High.Value.ShouldBe(110m);
        candle.Low.Value.ShouldBe(95m);
        candle.Close.Value.ShouldBe(105m);
    }

    [Theory]
    // High below open.
    [InlineData(100, 99, 95, 98)]
    // Low above close.
    [InlineData(100, 110, 106, 105)]
    // High below close.
    [InlineData(100, 104, 95, 105)]
    public void Rejects_a_range_where_high_or_low_is_not_the_extreme(
        decimal open, decimal high, decimal low, decimal close)
    {
        Should.Throw<DomainException>(() => Candle.Create(
                "BTCUSDT", "15m", OpenTime, CloseTime,
                open, high, low, close,
                volume: 1m, quoteVolume: 1m, tradeCount: 1, exchange: "binance"))
            .Code.ShouldBe("domain.candle.invalid_ohlc");
    }

    [Fact]
    public void Rejects_a_negative_volume()
    {
        Should.Throw<DomainException>(() => Candle.Create(
                "BTCUSDT", "15m", OpenTime, CloseTime,
                open: 100m, high: 100m, low: 100m, close: 100m,
                volume: -1m, quoteVolume: 1m, tradeCount: 1, exchange: "binance"))
            .Code.ShouldBe("domain.candle.negative_volume");
    }

    [Fact]
    public void Rejects_an_unsupported_timeframe()
    {
        Should.Throw<DomainException>(() => Candle.Create(
                "BTCUSDT", "7m", OpenTime, CloseTime,
                open: 100m, high: 100m, low: 100m, close: 100m,
                volume: 1m, quoteVolume: 1m, tradeCount: 1, exchange: "binance"))
            .Code.ShouldBe("domain.timeframe.unsupported");
    }
}
