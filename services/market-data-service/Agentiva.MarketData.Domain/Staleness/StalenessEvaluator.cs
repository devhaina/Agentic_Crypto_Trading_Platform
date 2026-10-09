namespace Agentiva.MarketData.Domain.Staleness;

/// <summary>Outcome of comparing a symbol's last data age against its threshold.</summary>
/// <param name="IsStale">Whether the data age exceeds the threshold.</param>
/// <param name="StaleFor">How long past the threshold the data is, zero while fresh.</param>
public readonly record struct StalenessStatus(bool IsStale, TimeSpan StaleFor);

/// <summary>
/// Pure comparison of a symbol's last-seen time against its staleness threshold.
/// </summary>
/// <remarks>
/// Kept free of any clock, cache or timer so the threshold comparison itself —
/// the part that decides whether the platform trusts a price — can be asserted
/// against fixed instants in a unit test, independent of wall-clock timing.
/// </remarks>
public static class StalenessEvaluator
{
    public static StalenessStatus Evaluate(DateTimeOffset lastReceivedAt, DateTimeOffset now, TimeSpan threshold)
    {
        var age = now - lastReceivedAt;

        return age <= threshold
            ? new StalenessStatus(IsStale: false, StaleFor: TimeSpan.Zero)
            : new StalenessStatus(IsStale: true, StaleFor: age - threshold);
    }
}
