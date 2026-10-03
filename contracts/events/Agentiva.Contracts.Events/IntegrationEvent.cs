namespace Agentiva.Contracts.Events;

/// <summary>
/// Base type for every event published on the Agentiva bus.
/// </summary>
/// <remarks>
/// Distinct from an in-process domain event: an integration event is a public,
/// versioned contract crossing a service boundary, so it carries only primitive
/// and string-typed data. Domain value objects are deliberately not used here —
/// a published contract must be consumable by the Python agent platform and the
/// Angular client, neither of which can deserialise a C# value object.
/// </remarks>
public abstract record IntegrationEvent
{
    protected IntegrationEvent()
    {
        EventId = Guid.CreateVersion7();
        OccurredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Unique identity of this occurrence. Consumers de-duplicate on it.</summary>
    public Guid EventId { get; init; }

    /// <summary>When the fact occurred, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Routing key, taken from <see cref="EventTypes"/>.</summary>
    public abstract string EventType { get; }

    /// <summary>
    /// Payload schema version. Incremented only on a breaking change, and only
    /// alongside a new routing key or a parallel consumer rollout.
    /// </summary>
    public virtual int Version => 1;

    /// <summary>
    /// End-to-end correlation identifier, propagated from the originating request
    /// so that one user action can be traced across every service it touched.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// The AI agent run that led to this event, when one did. Null for events
    /// originating from a deterministic strategy or an operator action.
    /// </summary>
    public string? AgentRunId { get; init; }
}
