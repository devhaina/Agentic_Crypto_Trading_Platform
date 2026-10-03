namespace Agentiva.BuildingBlocks.Domain.Abstractions;

/// <summary>
/// A fact that has already happened inside the domain.
/// </summary>
/// <remarks>
/// Raised by aggregates and collected by the unit of work, which writes them to
/// the outbox in the same database transaction as the state change. That is what
/// makes "state changed" and "event published" atomic — see the outbox pattern in
/// <c>Agentiva.BuildingBlocks.Persistence</c>.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Unique identity of this occurrence, used for consumer de-duplication.</summary>
    Guid EventId { get; }

    /// <summary>When the fact occurred, in UTC.</summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Routing key used on the RabbitMQ topic exchange, e.g. <c>trade.intent.created</c>.
    /// Part of the published contract; never change one in place.
    /// </summary>
    string EventType { get; }

    /// <summary>Schema version of the payload, incremented on a breaking change.</summary>
    int Version { get; }
}

/// <summary>Convenience base implementing the identity and timestamp of an event.</summary>
public abstract record DomainEvent : IDomainEvent
{
    protected DomainEvent(DateTimeOffset occurredAt)
    {
        // Version 7 GUIDs are time-ordered, which keeps the clustered index on
        // event tables append-only instead of scattering inserts across pages.
        EventId = Guid.CreateVersion7();
        OccurredAt = occurredAt;
    }

    public Guid EventId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public abstract string EventType { get; }

    public virtual int Version => 1;
}

/// <summary>
/// Implemented by aggregates that raise domain events, so that the persistence
/// layer can collect them without knowing each aggregate's identifier type.
/// </summary>
/// <remarks>
/// Declared here rather than in the persistence building block to keep the
/// dependency pointing inwards: the domain must not know that an outbox exists.
/// </remarks>
public interface IHasDomainEvents
{
    /// <summary>Events raised and not yet drained.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Removes and returns pending events. Called only by the unit of work.</summary>
    IReadOnlyCollection<IDomainEvent> DrainDomainEvents();
}
