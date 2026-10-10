using System.ComponentModel.DataAnnotations;

namespace Agentiva.Strategy.Infrastructure.Configuration;

/// <summary>Tuning for the strategy engine's in-memory candle window.</summary>
public sealed class StrategyOptions
{
    public const string SectionName = "Strategy";

    /// <summary>
    /// Closed bars kept per (symbol, timeframe). Must be at least as large as
    /// the longest lookback any registered strategy needs —
    /// <c>TrendFollowingStrategy</c>'s 200-period EMA is currently the binding
    /// one — or that strategy can never leave "insufficient data".
    /// </summary>
    [Range(50, 2000)]
    public int CandleBufferCapacity { get; set; } = 250;
}
