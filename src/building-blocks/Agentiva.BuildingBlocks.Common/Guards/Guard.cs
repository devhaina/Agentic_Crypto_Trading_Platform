using System.Runtime.CompilerServices;

namespace Agentiva.BuildingBlocks.Common.Guards;

/// <summary>
/// Precondition helpers for invariants that represent programmer error rather
/// than user input. User input is validated with FluentValidation and returns a
/// <see cref="Results.Result"/>; these throw, because reaching them means a bug.
/// </summary>
public static class Guard
{
    public static string AgainstNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be null or whitespace.", parameterName);
        }

        return value;
    }

    public static T AgainstNull<T>(
        T? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where T : class
        => value ?? throw new ArgumentNullException(parameterName);

    public static decimal AgainstNegative(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must not be negative.");
        }

        return value;
    }

    public static decimal AgainstNonPositive(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be greater than zero.");
        }

        return value;
    }

    public static int AgainstOutOfRange(
        int value,
        int min,
        int max,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"Value must be between {min} and {max}.");
        }

        return value;
    }
}
