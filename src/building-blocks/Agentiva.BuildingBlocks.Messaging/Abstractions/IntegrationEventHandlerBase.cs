using System.Text.Json;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.Contracts.Events;

namespace Agentiva.BuildingBlocks.Messaging.Abstractions;

/// <summary>
/// Base class turning a raw message body into a typed event before handling.
/// </summary>
/// <typeparam name="TEvent">The event type this handler consumes.</typeparam>
public abstract class IntegrationEventHandlerBase<TEvent> : IIntegrationEventHandler<TEvent>
    where TEvent : IntegrationEvent
{
    /// <inheritdoc />
    public abstract string EventType { get; }

    /// <inheritdoc />
    public abstract Task HandleAsync(
        TEvent integrationEvent,
        IntegrationEventContext context,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(string payload, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        TEvent? typed;

        try
        {
            typed = JsonSerializer.Deserialize<TEvent>(payload, AgentivaJson.Options);
        }
        catch (JsonException ex)
        {
            // A body that will not deserialise is poison: retrying cannot help,
            // because the bytes will not change. Wrapping it in a dedicated
            // exception lets the consumer dead-letter it with a clear reason
            // instead of burning all three retry tiers on a hopeless message.
            throw new IntegrationEventDeserializationException(
                $"Could not deserialise {context.RoutingKey} version {context.EventVersion} "
                + $"into {typeof(TEvent).Name}. The publisher's contract may have changed incompatibly.",
                ex);
        }

        return typed is null
            ? throw new IntegrationEventDeserializationException(
                $"Payload for {context.RoutingKey} deserialised to null.", null)
            : HandleAsync(typed, context, cancellationToken);
    }
}

/// <summary>Raised when a message body cannot be deserialised into its contract type.</summary>
public sealed class IntegrationEventDeserializationException(string message, Exception? innerException)
    : Exception(message, innerException);
