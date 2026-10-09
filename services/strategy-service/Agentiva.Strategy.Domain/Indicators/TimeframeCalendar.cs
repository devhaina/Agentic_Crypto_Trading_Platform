namespace Agentiva.Strategy.Domain.Indicators;

/// <summary>Maps a Binance kline interval onto how many of that bar fit in a calendar year.</summary>
/// <remarks>
/// Exists solely to annualise a per-bar volatility estimate — see
/// <see cref="IndicatorEngine.AnnualizedVolatilityPercent"/>. Deliberately not
/// the Market Data Service's <c>Timeframe</c> value object: this is calendar
/// arithmetic, not a validated domain concept, and duplicating a short,
/// stable lookup table is cheaper than depending on another service's domain
/// type for it.
/// </remarks>
internal static class TimeframeCalendar
{
    public static int? BarsPerYear(string timeframe) => timeframe switch
    {
        "1m" => 365 * 24 * 60,
        "3m" => 365 * 24 * 20,
        "5m" => 365 * 24 * 12,
        "15m" => 365 * 24 * 4,
        "30m" => 365 * 24 * 2,
        "1h" => 365 * 24,
        "2h" => 365 * 12,
        "4h" => 365 * 6,
        "6h" => 365 * 4,
        "8h" => 365 * 3,
        "12h" => 365 * 2,
        "1d" => 365,
        "3d" => 121,
        "1w" => 52,
        "1M" => 12,
        _ => null
    };
}
