using Agentiva.BuildingBlocks.Domain.Exceptions;

namespace Agentiva.MarketData.Domain.Primitives;

/// <summary>
/// A candle interval, e.g. <c>15m</c> or <c>1h</c>.
/// </summary>
/// <remarks>
/// Restricted to Binance's own kline intervals rather than any string that
/// matches a plausible pattern. An interval Binance does not support would
/// never produce a kline stream, so a candle for it can only ever be an
/// aggregation this service invented itself — and no component here computes
/// candles from trades. Validating against the exchange's own vocabulary turns
/// a silently-empty feed into a loud startup failure instead.
/// </remarks>
public readonly record struct Timeframe
{
    /// <summary>Every interval Binance's kline streams support, in ascending order.</summary>
    private static readonly string[] SupportedIntervals =
    [
        "1m", "3m", "5m", "15m", "30m",
        "1h", "2h", "4h", "6h", "8h", "12h",
        "1d", "3d", "1w", "1M"
    ];

    private Timeframe(string value) => Value = value;

    /// <summary>The interval text, exactly as Binance expects it on the wire.</summary>
    public string Value { get; }

    /// <summary>Parses and validates an interval, throwing when unsupported.</summary>
    /// <exception cref="DomainException">The value is empty or not a Binance kline interval.</exception>
    public static Timeframe Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("domain.timeframe.empty", "Timeframe must not be empty.");
        }

        var trimmed = value.Trim();

        if (!Array.Exists(SupportedIntervals, candidate => string.Equals(candidate, trimmed, StringComparison.Ordinal)))
        {
            throw new DomainException(
                "domain.timeframe.unsupported",
                $"Timeframe '{value}' is not a Binance kline interval. Supported: "
                + string.Join(", ", SupportedIntervals));
        }

        return new Timeframe(trimmed);
    }

    public override string ToString() => Value;

    public static implicit operator string(Timeframe timeframe) => timeframe.Value;
}
