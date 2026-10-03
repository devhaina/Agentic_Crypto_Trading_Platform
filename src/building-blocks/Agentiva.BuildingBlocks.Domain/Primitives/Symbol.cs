using Agentiva.BuildingBlocks.Domain.Exceptions;

namespace Agentiva.BuildingBlocks.Domain.Primitives;

/// <summary>
/// A normalised trading pair symbol, e.g. <c>BTCUSDT</c>.
/// </summary>
/// <remarks>
/// Exists to prevent primitive obsession on the single most-passed value in the
/// platform. A bare <c>string</c> permits <c>"btcusdt"</c>, <c>"BTC-USDT"</c> and
/// <c>"BTC/USDT"</c> to coexist and silently fail equality comparisons — which in
/// a trading system means a position lookup misses and the risk engine sees zero
/// exposure. Construction normalises to upper-case and validates the character
/// set, so two symbols that mean the same market are always equal.
/// </remarks>
public readonly record struct Symbol
{
    private Symbol(string value) => Value = value;

    /// <summary>Normalised, upper-case symbol text.</summary>
    public string Value { get; }

    /// <summary>Parses and normalises a symbol, throwing when malformed.</summary>
    /// <exception cref="DomainException">The value is empty or contains unsupported characters.</exception>
    public static Symbol Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("domain.symbol.empty", "Symbol must not be empty.");
        }

        var normalised = value.Trim().ToUpperInvariant();

        // Exchange symbols are alphanumeric. Reject separators rather than
        // stripping them: "BTC-USDT" and "BTCU-SDT" would strip to the same
        // value, so silently rewriting the input could mask a real mistake.
        foreach (var c in normalised)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                throw new DomainException(
                    "domain.symbol.invalid_characters",
                    $"Symbol '{value}' contains unsupported characters; expected alphanumeric only.");
            }
        }

        if (normalised.Length is < 5 or > 24)
        {
            throw new DomainException(
                "domain.symbol.invalid_length",
                $"Symbol '{value}' has an implausible length of {normalised.Length}.");
        }

        return new Symbol(normalised);
    }

    /// <summary>Attempts to parse a symbol without throwing.</summary>
    public static bool TryCreate(string? value, out Symbol symbol)
    {
        try
        {
            symbol = Create(value!);
            return true;
        }
        catch (DomainException)
        {
            symbol = default;
            return false;
        }
    }

    public override string ToString() => Value;

    public static implicit operator string(Symbol symbol) => symbol.Value;
}
