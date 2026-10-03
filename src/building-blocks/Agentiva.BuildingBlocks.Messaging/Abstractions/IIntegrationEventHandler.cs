using Agentiva.Contracts.Events;

namespace Agentiva.BuildingBlocks.Messaging.Abstractions;

/// <summary>Marker for the non-generic consumer dispatch path.</summary>
public interface IIntegrationEventHandler
{
    /// <summary>Routing key this handler subscribes to.</summary>
    string EventType { get; }

    /// <summary>Handles a raw message body.</summary>
    /// <remarks>
    /// Implementations must be idempotent. RabbitMQ guarantees at-least-once
    /// delivery, so a handler will occasionally see the same event twice — on a
    /// redelivery after a failed acknowledgement, or during a retry.
    /// </remarks>
    Task HandleAsync(string payload, IntegrationEventContext context, CancellationToken cancellationToken);
}

/// <summary>Typed handler for one integration event.</summary>
/// <typeparam name="TEvent">The event type.</typeparam>
public interface IIntegrationEventHandler<in TEvent> : IIntegrationEventHandler
    where TEvent : IntegrationEvent
{
    /// <summary>Handles a deserialised event. Must be idempotent.</summary>
    Task HandleAsync(TEvent integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken);
}

/// <summary>Delivery metadata for a consumed message.</summary>
/// <param name="EventId">Event identity, for de-duplication.</param>
/// <param name="RoutingKey">The routing key it arrived on.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
/// <param name="AgentRunId">Originating AI agent run, when applicable.</param>
/// <param name="Attempt">1 on first delivery, incremented on each retry.</param>
/// <param name="EventVersion">Payload schema version.</param>
public sealed record IntegrationEventContext(
    Guid EventId,
    string RoutingKey,
    string CorrelationId,
    string? AgentRunId,
    int Attempt,
    int EventVersion);
