using Agentiva.Contracts.Events;

namespace Agentiva.BuildingBlocks.Messaging.Abstractions;

/// <summary>Publishes integration events onto the event bus.</summary>
/// <remarks>
/// Application code normally does not call this directly. Domain state changes
/// raise domain events, which the unit of work writes to the outbox in the same
/// transaction; the outbox processor is what ultimately calls this. Publishing
/// inline would reintroduce the dual-write problem the outbox exists to solve.
/// </remarks>
public interface IEventPublisher
{
    /// <summary>Publishes one event, waiting for a broker confirmation.</summary>
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent;

    /// <summary>
    /// Publishes a pre-serialised payload. Used by the outbox processor, which
    /// stores the JSON body rather than a CLR type.
    /// </summary>
    /// <param name="routingKey">Routing key from <see cref="EventTypes"/>.</param>
    /// <param name="payload">Serialised event body.</param>
    /// <param name="eventId">Event identity, used as the AMQP message id for de-duplication.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <param name="eventVersion">Payload schema version.</param>
    /// <param name="agentRunId">Originating AI agent run, when applicable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishRawAsync(
        string routingKey,
        string payload,
        Guid eventId,
        string correlationId,
        int eventVersion,
        string? agentRunId = null,
        CancellationToken cancellationToken = default);
}
