namespace Agentiva.BuildingBlocks.Domain.Exceptions;

/// <summary>
/// Raised when an operation would leave a domain object in an invalid state.
/// </summary>
/// <remarks>
/// Signals a programming error or an unvalidated input path, not an expected
/// business outcome. Expected outcomes (a rejected risk check, an unknown
/// symbol) are modelled with <c>Result</c> instead.
/// </remarks>
public class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
        => Code = code;

    public DomainException(string code, string message, Exception innerException)
        : base(message, innerException)
        => Code = code;

    /// <summary>Stable reason code for dashboards and audit records.</summary>
    public string Code { get; } = string.Empty;
}
