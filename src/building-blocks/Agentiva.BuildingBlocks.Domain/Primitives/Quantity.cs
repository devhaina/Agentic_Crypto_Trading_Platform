using Agentiva.BuildingBlocks.Domain.Exceptions;

namespace Agentiva.BuildingBlocks.Domain.Primitives;

/// <summary>
/// An amount of a base asset, e.g. 0.01 BTC. Always non-negative;
/// direction is carried by <see cref="OrderSide"/>, never by the sign.
/// </summary>
/// <remarks>
/// Backed by <see cref="decimal"/>. A <c>double</c> cannot represent 0.1
/// exactly, so repeatedly accumulating fills in binary floating point drifts
/// away from the true position size — and the drift is unbounded over a
/// trading day. <see cref="decimal"/> is base-10 and exact for these values.
/// </remarks>
public readonly record struct Quantity : IComparable<Quantity>
{
    private Quantity(decimal value) => Value = value;

    public decimal Value { get; }

    public static Quantity Zero => new(0m);

    public bool IsZero => Value == 0m;

    /// <summary>Creates a quantity, rejecting negative input.</summary>
    /// <exception cref="DomainException">The value is negative.</exception>
    public static Quantity Create(decimal value)
        => value < 0m
            ? throw new DomainException("domain.quantity.negative", $"Quantity must not be negative but was {value}.")
            : new Quantity(value);

    public static Quantity operator +(Quantity left, Quantity right) => new(left.Value + right.Value);

    /// <summary>Subtracts quantities, rejecting a negative outcome.</summary>
    /// <exception cref="DomainException">The result would be negative.</exception>
    public static Quantity operator -(Quantity left, Quantity right)
        => left.Value < right.Value
            ? throw new DomainException(
                "domain.quantity.negative_result",
                $"Subtracting {right.Value} from {left.Value} would yield a negative quantity.")
            : new Quantity(left.Value - right.Value);

    public static Quantity operator *(Quantity quantity, decimal multiplier)
        => Create(quantity.Value * multiplier);

    public static bool operator <(Quantity left, Quantity right) => left.Value < right.Value;

    public static bool operator >(Quantity left, Quantity right) => left.Value > right.Value;

    public static bool operator <=(Quantity left, Quantity right) => left.Value <= right.Value;

    public static bool operator >=(Quantity left, Quantity right) => left.Value >= right.Value;

    public int CompareTo(Quantity other) => Value.CompareTo(other.Value);

    /// <summary>Invariant-culture round-trip form, safe for logs and wire payloads.</summary>
    public override string ToString() => Value.ToString("0.##################", System.Globalization.CultureInfo.InvariantCulture);
}
