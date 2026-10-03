namespace Agentiva.BuildingBlocks.Persistence.Inbox;

/// <summary>
/// Record that one consumer has processed one event, making at-least-once
/// delivery into effectively-once processing.
/// </summary>
/// <remarks>
/// Keyed on (<see cref="EventId"/>, <see cref="ConsumerName"/>) rather than
/// event id alone, because several handlers in the same service legitimately
/// consume the same event and each must run exactly once.
/// </remarks>
public sealed class InboxMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Identity of the processed event.</summary>
    public Guid EventId { get; set; }

    /// <summary>Fully qualified handler type name.</summary>
    public string ConsumerName { get; set; } = string.Empty;

    /// <summary>Routing key the event arrived on, retained for diagnosis.</summary>
    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }
}
