using System.ComponentModel.DataAnnotations;

namespace Agentiva.MarketData.Infrastructure.Configuration;

/// <summary>What this service ingests, and how stale it is allowed to get.</summary>
public sealed class MarketDataOptions
{
    public const string SectionName = "MarketData";

    /// <summary>Comma-separated symbols to ingest, e.g. <c>BTCUSDT,ETHUSDT</c>.</summary>
    [Required]
    public string Symbols { get; set; } = string.Empty;

    /// <summary>Comma-separated kline intervals to ingest, e.g. <c>15m,1h</c>.</summary>
    [Required]
    public string Timeframes { get; set; } = string.Empty;

    /// <summary>
    /// How old a symbol's data may get, across every stream, before a
    /// <c>MarketDataStale</c> event is published for it.
    /// </summary>
    [Range(1, 3600)]
    public int StalenessThresholdSeconds { get; set; } = 30;

    /// <summary>How often the staleness monitor re-checks every symbol.</summary>
    [Range(1, 60)]
    public int StalenessCheckIntervalSeconds { get; set; } = 5;

    /// <summary>Parses <see cref="Symbols"/> into a normalised (upper-case), de-duplicated list.</summary>
    public IReadOnlyList<string> SymbolList
        => Split(Symbols).Select(s => s.ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Parses <see cref="Timeframes"/> into a de-duplicated list, case preserved.
    /// </summary>
    /// <remarks>
    /// Binance kline intervals are case-sensitive in a way that changes their
    /// meaning: <c>1m</c> is one minute and <c>1M</c> is one month.
    /// Upper-casing this list the way <see cref="SymbolList"/> upper-cases
    /// symbols would silently turn every monthly candle request into a
    /// minute one.
    /// </remarks>
    public IReadOnlyList<string> TimeframeList
        => Split(Timeframes).Distinct(StringComparer.Ordinal).ToArray();

    private static string[] Split(string value)
        => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
