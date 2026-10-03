namespace Agentiva.BuildingBlocks.Common.Results;

/// <summary>
/// A machine-readable failure. Every failure in the platform carries a stable
/// <see cref="Code"/> so that operators, dashboards and audit records can be
/// keyed on something other than a human-readable string.
/// </summary>
/// <param name="Code">
/// Stable, dot-delimited reason code, e.g. <c>risk.rejected.max_daily_loss</c>.
/// Treated as part of the public contract: never reword an existing code.
/// </param>
/// <param name="Message">Operator-facing description. Safe to log.</param>
/// <param name="Type">Failure classification, used to map onto HTTP status codes.</param>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    /// <summary>Sentinel used by <see cref="Result"/> when there is no failure.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>A downstream dependency (exchange, service, broker) was unavailable.</summary>
    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);

    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Classification of an <see cref="Error"/>, used for transport mapping.</summary>
public enum ErrorType
{
    None = 0,
    Failure = 1,
    Validation = 2,
    NotFound = 3,
    Conflict = 4,
    Unauthorized = 5,
    Forbidden = 6,
    Unavailable = 7
}
