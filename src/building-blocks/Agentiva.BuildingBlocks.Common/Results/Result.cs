using System.Diagnostics.CodeAnalysis;

namespace Agentiva.BuildingBlocks.Common.Results;

/// <summary>
/// Outcome of an operation that can fail for an expected, domain-level reason.
/// </summary>
/// <remarks>
/// <para>
/// Used instead of exceptions for <em>expected</em> failures — a rejected risk
/// check, an unknown symbol, an insufficient balance. Exceptions remain reserved
/// for genuinely exceptional conditions (bugs, infrastructure faults), which keeps
/// the financial decision path explicit and cheap: a risk rejection is a normal
/// business outcome and must not depend on stack unwinding.
/// </para>
/// </remarks>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // Guard against the two nonsensical states: a success carrying an error,
        // and a failure carrying none. Both would silently corrupt call sites.
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>Outcome of an operation that yields a value on success.</summary>
/// <typeparam name="TValue">The success payload type.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
        => _value = value;

    /// <summary>The success payload.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a failure.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    /// <summary>Non-throwing accessor, for call sites that branch on success.</summary>
    public bool TryGetValue([NotNullWhen(true)] out TValue? value)
    {
        value = IsSuccess ? _value : default;
        return IsSuccess;
    }

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}
