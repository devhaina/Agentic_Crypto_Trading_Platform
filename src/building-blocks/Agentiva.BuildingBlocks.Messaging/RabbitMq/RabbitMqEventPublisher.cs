using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.BuildingBlocks.Messaging.Configuration;
using Agentiva.Contracts.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Agentiva.BuildingBlocks.Messaging.RabbitMq;

/// <summary>
/// Publishes events to the main topic exchange with publisher confirmations.
/// </summary>
/// <remarks>
/// <para>
/// Publisher confirmations are on and awaited. Without them, <c>BasicPublish</c>
/// is fire-and-forget: it returns as soon as the bytes reach the socket, so a
/// broker that dies mid-flight loses the event while the publisher believes it
/// succeeded. With confirmations awaited, a lost publish surfaces as an
/// exception, the outbox row stays unsent, and the processor retries it.
/// </para>
/// <para>
/// Messages are marked persistent so they survive a broker restart. Persistence
/// and confirmation are independent settings and both are required: a confirmed
/// transient message is still lost when the broker restarts.
/// </para>
/// </remarks>
public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private static readonly ActivitySource ActivitySource = new("Agentiva.Messaging.Publisher");

    private readonly IRabbitMqConnectionProvider _connectionProvider;
    private readonly ICorrelationContext _correlation;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly SemaphoreSlim _channelGate = new(1, 1);
    private IChannel? _channel;
    private bool _disposed;

    public RabbitMqEventPublisher(
        IRabbitMqConnectionProvider connectionProvider,
        ICorrelationContext correlation,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connectionProvider = connectionProvider;
        _correlation = correlation;
        _logger = logger;
        _ = options;
    }

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), AgentivaJson.Options);

        var correlationId = string.IsNullOrEmpty(integrationEvent.CorrelationId)
            ? _correlation.CorrelationId
            : integrationEvent.CorrelationId;

        return PublishRawAsync(
            integrationEvent.EventType,
            payload,
            integrationEvent.EventId,
            correlationId,
            integrationEvent.Version,
            integrationEvent.AgentRunId,
            cancellationToken);
    }

    public async Task PublishRawAsync(
        string routingKey,
        string payload,
        Guid eventId,
        string correlationId,
        int eventVersion,
        string? agentRunId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var activity = ActivitySource.StartActivity($"publish {routingKey}", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", EventBusTopology.MainExchange);
        activity?.SetTag("messaging.rabbitmq.routing_key", routingKey);
        activity?.SetTag("messaging.message.id", eventId.ToString());

        var channel = await GetChannelAsync(cancellationToken);

        var properties = new BasicProperties
        {
            MessageId = eventId.ToString(),
            CorrelationId = correlationId,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            Type = routingKey,

            // Survive a broker restart.
            Persistent = true,

            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = BuildHeaders(correlationId, agentRunId, eventVersion, activity)
        };

        var body = Encoding.UTF8.GetBytes(payload);

        // Serialised: a single IChannel must not have concurrent publishes in
        // flight, and the confirmation tracking is per-channel state.
        await _channelGate.WaitAsync(cancellationToken);

        try
        {
            // Awaiting this waits for the broker's ack because the channel was
            // created with publisher confirmation tracking enabled.
            await channel.BasicPublishAsync(
                exchange: EventBusTopology.MainExchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _channelGate.Release();
        }

        _logger.LogDebug(
            "Published {RoutingKey} with event id {EventId} and correlation id {CorrelationId}",
            routingKey,
            eventId,
            correlationId);
    }

    private static Dictionary<string, object?> BuildHeaders(
        string correlationId,
        string? agentRunId,
        int eventVersion,
        Activity? activity)
    {
        var headers = new Dictionary<string, object?>
        {
            [EventBusTopology.Headers.CorrelationId] = correlationId,
            [EventBusTopology.Headers.EventVersion] = eventVersion
        };

        if (!string.IsNullOrEmpty(agentRunId))
        {
            headers[EventBusTopology.Headers.AgentRunId] = agentRunId;
        }

        // Carry W3C trace context across the broker so a trace started in
        // Angular still joins up in the consumer's span.
        if (activity is not null)
        {
            headers[EventBusTopology.Headers.TraceParent] = activity.Id;
        }

        return headers;
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _channelGate.WaitAsync(cancellationToken);

        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            if (_channel is not null)
            {
                await _channel.DisposeAsync();
            }

            var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);

            _channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                cancellationToken);

            return _channel;
        }
        finally
        {
            _channelGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_channel is not null)
        {
            try
            {
                await _channel.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing the publisher channel during shutdown.");
            }

            await _channel.DisposeAsync();
        }

        _channelGate.Dispose();
    }
}
