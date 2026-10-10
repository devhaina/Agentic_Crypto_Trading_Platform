using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agentiva.BuildingBlocks.Domain.Exceptions;

namespace Agentiva.BuildingBlocks.Domain.Primitives;

/// <summary>
/// A percentage, stored in percent units: <c>Percentage.FromPercent(0.5)</c> is
/// one half of one percent.
/// </summary>
/// <remarks>
/// <para>
/// This type exists to kill one specific and very expensive class of bug. When a
/// risk limit travels as a bare <c>decimal</c>, nothing distinguishes "0.5 meaning
/// half a percent" from "0.5 meaning fifty percent". The two readings differ by a
/// factor of one hundred, and the direction of the error is to risk 100× the
/// intended amount per trade.
/// </para>
/// <para>
/// The remedy is that the unit is part of the type. There is no implicit
/// conversion: a caller must say <see cref="FromPercent"/> or
/// <see cref="FromFraction"/> on the way in, and read either
/// <see cref="Percent"/> or <see cref="AsFraction"/> on the way out. Arithmetic
/// against money always goes through <see cref="AsFraction"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(PercentageJsonConverter))]
public readonly record struct Percentage : IComparable<Percentage>
{
    private Percentage(decimal percent) => Percent = percent;

    /// <summary>The value in percent units (50 means fifty percent).</summary>
    public decimal Percent { get; }

    /// <summary>The value as a multiplier fraction (fifty percent yields 0.5).</summary>
    public decimal AsFraction => Percent / 100m;

    public static Percentage Zero => new(0m);

    /// <summary>Creates a percentage from percent units, e.g. <c>0.5</c> for half a percent.</summary>
    /// <exception cref="DomainException">The value is negative or exceeds 100%.</exception>
    public static Percentage FromPercent(decimal percent)
    {
        if (percent < 0m)
        {
            throw new DomainException(
                "domain.percentage.negative", $"Percentage must not be negative but was {percent}.");
        }

        // Every percentage in this platform is a risk limit, an exposure cap, a
        // fee or a confidence level, and none of those exceed 100%. Rejecting
        // >100 catches the fraction/percent mix-up at the boundary.
        if (percent > 100m)
        {
            throw new DomainException(
                "domain.percentage.exceeds_one_hundred",
                $"Percentage must not exceed 100 but was {percent}. "
                + "If this value is a fraction, use Percentage.FromFraction instead.");
        }

        return new Percentage(percent);
    }

    /// <summary>Creates a percentage from a fraction, e.g. <c>0.005</c> for half a percent.</summary>
    /// <exception cref="DomainException">The fraction is negative or exceeds 1.</exception>
    public static Percentage FromFraction(decimal fraction)
    {
        if (fraction is < 0m or > 1m)
        {
            throw new DomainException(
                "domain.percentage.fraction_out_of_range",
                $"Fraction must be between 0 and 1 but was {fraction}.");
        }

        return new Percentage(fraction * 100m);
    }

    /// <summary>Applies this percentage to an amount.</summary>
    public decimal Of(decimal amount) => amount * AsFraction;

    public static bool operator <(Percentage left, Percentage right) => left.Percent < right.Percent;

    public static bool operator >(Percentage left, Percentage right) => left.Percent > right.Percent;

    public static bool operator <=(Percentage left, Percentage right) => left.Percent <= right.Percent;

    public static bool operator >=(Percentage left, Percentage right) => left.Percent >= right.Percent;

    public int CompareTo(Percentage other) => Percent.CompareTo(other.Percent);

    public override string ToString() => $"{Percent.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}%";
}

/// <summary>
/// Reads and writes a <see cref="Percentage"/> as a single JSON string of its
/// percent value, e.g. <c>"42.5"</c>.
/// </summary>
/// <remarks>
/// Without this, <see cref="JsonSerializer"/>'s default reflection-based
/// converter writes <see cref="Percentage"/> correctly (a JSON object with
/// its two read-only properties) but cannot read it back: the type has no
/// public constructor or settable property a deserializer can use, so it
/// silently materialises a zeroed <c>default(Percentage)</c> instead of
/// throwing — a value quietly becomes zero, with no error anywhere, the
/// first time this type is serialised to JSON and read back rather than
/// passed through <c>PercentageConverter</c> (the EF-only, decimal-column
/// counterpart). Caught only by an end-to-end run that actually persisted a
/// value through this path and read it back, not by any unit test against
/// an in-memory object.
/// </remarks>
public sealed class PercentageJsonConverter : JsonConverter<Percentage>
{
    public override Percentage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var percent = reader.TokenType switch
        {
            JsonTokenType.String => decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDecimal(),
            _ => throw new JsonException(
                $"Expected a JSON string or number for {nameof(Percentage)} but found {reader.TokenType}.")
        };

        return Percentage.FromPercent(percent);
    }

    public override void Write(Utf8JsonWriter writer, Percentage value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Percent.ToString(CultureInfo.InvariantCulture));
}
