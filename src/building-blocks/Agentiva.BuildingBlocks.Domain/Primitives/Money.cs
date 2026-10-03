using Agentiva.BuildingBlocks.Domain.Exceptions;

namespace Agentiva.BuildingBlocks.Domain.Primitives;

/// <summary>
/// An amount of a specific asset. Unlike <see cref="Quantity"/>, a money amount
/// may be negative — a realised loss and a negative P&amp;L are legitimate values.
/// </summary>
/// <remarks>
/// The currency travels with the amount so that adding USDT to BTC is a
/// compile-time-shaped, run-time-enforced error rather than a plausible-looking
/// number. Mixing denominations is the classic way a portfolio valuation ends up
/// confidently wrong.
/// </remarks>
public readonly record struct Money : IComparable<Money>
{
    private Money(decimal amount, AssetCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public AssetCode Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public static Money Create(decimal amount, AssetCode currency) => new(amount, currency);

    public static Money Zero(AssetCode currency) => new(0m, currency);

    /// <summary>Creates a money amount, rejecting negative input.</summary>
    /// <exception cref="DomainException">The amount is negative.</exception>
    public static Money CreateNonNegative(decimal amount, AssetCode currency)
        => amount < 0m
            ? throw new DomainException(
                "domain.money.negative", $"Amount must not be negative but was {amount} {currency}.")
            : new Money(amount, currency);

    public Money Abs() => new(Math.Abs(Amount), Currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "add");
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "subtract");
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    public static Money operator *(Money money, decimal multiplier) => new(money.Amount * multiplier, money.Currency);

    /// <summary>Applies a percentage, e.g. computing a fee or a risk budget.</summary>
    public Money Percent(Percentage percentage) => new(percentage.Of(Amount), Currency);

    /// <summary>
    /// Expresses this amount as a percentage of a total. Returns
    /// <see cref="Percentage.Zero"/> when the total is zero, since an exposure
    /// ratio against an empty portfolio is defined as zero rather than infinite.
    /// </summary>
    public Percentage RatioOf(Money total)
    {
        EnsureSameCurrency(this, total, "compare");

        if (total.Amount == 0m)
        {
            return Percentage.Zero;
        }

        var percent = Amount / total.Amount * 100m;

        // Exposure can legitimately exceed the portfolio value (leverage) or be
        // negative (a loss), neither of which Percentage.FromPercent accepts.
        // Clamp to the representable band; callers compare against limits, and a
        // clamped 100% still trips every cap that matters.
        return percent switch
        {
            < 0m => Percentage.Zero,
            > 100m => Percentage.FromPercent(100m),
            _ => Percentage.FromPercent(percent)
        };
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "compare");
        return left.Amount < right.Amount;
    }

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "compare");
        return left.Amount > right.Amount;
    }

    public static bool operator <=(Money left, Money right) => !(left > right);

    public static bool operator >=(Money left, Money right) => !(left < right);

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other, "compare");
        return Amount.CompareTo(other.Amount);
    }

    private static void EnsureSameCurrency(Money left, Money right, string operation)
    {
        if (left.Currency != right.Currency)
        {
            throw new DomainException(
                "domain.money.currency_mismatch",
                $"Cannot {operation} {left.Currency} and {right.Currency}. "
                + "Convert through a valuation service before combining denominations.");
        }
    }

    public override string ToString()
        => $"{Amount.ToString("0.##################", System.Globalization.CultureInfo.InvariantCulture)} {Currency}";
}
