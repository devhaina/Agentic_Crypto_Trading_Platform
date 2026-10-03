using Agentiva.BuildingBlocks.Domain.Exceptions;

namespace Agentiva.BuildingBlocks.Domain.Primitives;

/// <summary>
/// A price expressed in quote asset per one unit of base asset,
/// e.g. 100250.50 USDT per BTC. Always strictly positive.
/// </summary>
public readonly record struct Price : IComparable<Price>
{
    private Price(decimal value) => Value = value;

    public decimal Value { get; }

    /// <summary>Creates a price, rejecting zero and negative input.</summary>
    /// <exception cref="DomainException">The value is not strictly positive.</exception>
    public static Price Create(decimal value)
        => value <= 0m
            ? throw new DomainException("domain.price.not_positive", $"Price must be greater than zero but was {value}.")
            : new Price(value);

    /// <summary>Absolute distance between two prices, as used for stop-loss sizing.</summary>
    public decimal DistanceTo(Price other) => Math.Abs(Value - other.Value);

    /// <summary>
    /// Applies a slippage assumption in the direction that is unfavourable to
    /// the trader: a buy is assumed to fill higher, a sell lower.
    /// </summary>
    /// <remarks>
    /// Always modelling slippage against the trader keeps position sizing
    /// conservative; assuming a favourable fill would understate risk.
    /// </remarks>
    public Price WithAdverseSlippage(Percentage slippage, OrderSide side)
    {
        var factor = side == OrderSide.Buy
            ? 1m + slippage.AsFraction
            : 1m - slippage.AsFraction;

        var adjusted = Value * factor;

        // A slippage assumption at or above 100% would drive a sell price to zero
        // or below. Treat as a configuration error rather than produce nonsense.
        return adjusted <= 0m
            ? throw new DomainException(
                "domain.price.slippage_too_large",
                $"Slippage of {slippage} applied to a {side} at {Value} yields a non-positive price.")
            : new Price(adjusted);
    }

    public static bool operator <(Price left, Price right) => left.Value < right.Value;

    public static bool operator >(Price left, Price right) => left.Value > right.Value;

    public static bool operator <=(Price left, Price right) => left.Value <= right.Value;

    public static bool operator >=(Price left, Price right) => left.Value >= right.Value;

    public int CompareTo(Price other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("0.##################", System.Globalization.CultureInfo.InvariantCulture);
}
