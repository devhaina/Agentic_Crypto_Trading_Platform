namespace Agentiva.BuildingBlocks.Persistence.Outbox;

/// <summary>Delivery state of an outbox row.</summary>
public enum OutboxStatus
{
    /// <summary>Written with the state change, not yet published.</summary>
    Pending = 1,

    /// <summary>Confirmed by the broker.</summary>
    Published = 2,

    /// <summary>Publishing failed repeatedly; parked for operator attention.</summary>
    Failed = 3
}

/// <summary>
/// An integration event awaiting publication, written in the same transaction as
/// the state change that produced it.
/// </summary>
/// <remarks>
/// <para>
/// This table exists to remove a dual write. Persisting an order and publishing
/// <c>order.created</c> are two separate systems, and without a shared
/// transaction one can succeed while the other fails:
/// </para>
/// <list type="bullet">
///   <item><description>
///     Publish first, then commit — the broker has told the portfolio service
///     about an order that the database rolled back. The portfolio now tracks a
///     position that does not exist.
///   </description></item>
///   <item><description>
///     Commit first, then publish — the order exists but nothing downstream ever
///     hears about it. The position is never opened, the stop is never placed.
///   </description></item>
/// </list>
/// <para>
/// Writing the event into this table inside the business transaction makes the
/// two atomic. A separate processor then moves rows to the broker with
/// at-least-once delivery, which is why every consumer de-duplicates on
/// <see cref="EventId"/>.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Row identity. Version 7, so inserts stay sequential in the index.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Event identity, carried as the AMQP message id for consumer de-duplication.</summary>
    public Guid EventId { get; set; }

    /// <summary>Routing key, e.g. <c>order.filled</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Payload schema version.</summary>
    public int EventVersion { get; set; } = 1;

    /// <summary>Serialised event body.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>End-to-end correlation identifier.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Originating AI agent run, when applicable.</summary>
    public string? AgentRunId { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the broker confirmed the publish.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Publish attempts so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Why the most recent attempt failed.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Earliest time the next attempt may run. Set on failure to back off,
    /// so a broken broker is not hammered by a tight loop.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}
