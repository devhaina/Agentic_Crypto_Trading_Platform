namespace Agentiva.BuildingBlocks.Application.Exceptions;

/// <summary>
/// Raised when an inbound request fails validation. Mapped to HTTP 400 with an
/// RFC 9457 problem document by the API exception middleware.
/// </summary>
public sealed class AgentivaValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    /// <summary>Validation failures keyed by property name.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
