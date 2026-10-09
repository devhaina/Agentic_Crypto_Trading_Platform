using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.MarketData.Domain.Primitives;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.MarketData;

public sealed class TimeframeTests
{
    [Theory]
    [InlineData("1m")]
    [InlineData("15m")]
    [InlineData("1h")]
    [InlineData("1d")]
    [InlineData("1M")]
    public void Accepts_every_Binance_kline_interval(string value)
        => Timeframe.Create(value).Value.ShouldBe(value);

    [Fact]
    public void Rejects_an_interval_Binance_does_not_support()
        => Should.Throw<DomainException>(() => Timeframe.Create("7m"))
            .Code.ShouldBe("domain.timeframe.unsupported");

    [Fact]
    public void Treats_minutes_and_months_as_distinct_despite_differing_only_by_case()
    {
        // "1m" is one minute and "1M" is one month on Binance's own wire
        // format. If this ever throws, something upstream has started
        // normalising case and silently changing what a request means.
        Timeframe.Create("1m").Value.ShouldBe("1m");
        Timeframe.Create("1M").Value.ShouldBe("1M");
    }

    [Fact]
    public void Rejects_an_empty_value()
        => Should.Throw<DomainException>(() => Timeframe.Create(string.Empty))
            .Code.ShouldBe("domain.timeframe.empty");
}
