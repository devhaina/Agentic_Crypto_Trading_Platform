using Agentiva.MarketData.Domain.Staleness;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.MarketData;

public sealed class StalenessEvaluatorTests
{
    private static readonly DateTimeOffset LastReceivedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    [Fact]
    public void Is_not_stale_at_exactly_the_threshold()
    {
        var status = StalenessEvaluator.Evaluate(LastReceivedAt, LastReceivedAt + Threshold, Threshold);

        status.IsStale.ShouldBeFalse();
        status.StaleFor.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Is_stale_one_tick_past_the_threshold()
    {
        var now = LastReceivedAt + Threshold + TimeSpan.FromSeconds(1);

        var status = StalenessEvaluator.Evaluate(LastReceivedAt, now, Threshold);

        status.IsStale.ShouldBeTrue();
        status.StaleFor.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Is_never_stale_before_any_data_has_aged()
    {
        var status = StalenessEvaluator.Evaluate(LastReceivedAt, LastReceivedAt, Threshold);

        status.IsStale.ShouldBeFalse();
    }
}
