using System.Diagnostics;
using System.Text;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.BuildingBlocks.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Agentiva.BuildingBlocks.Messaging.RabbitMq;

/// <summary>
/// Drains this service's work queue, dispatching each message to its registered
/// handlers with de-duplication, tiered retry and dead-lettering.
/// </summary>
/// <remarks>
/// <para>
/// Acknowledgement is manual and happens only after a handler has committed. With
/// auto-acknowledgement the broker considers a message delivered the moment it
/// hits the socket, so a consumer crash mid-handler loses the event silently —
/// for an <c>order.filled</c> event that means a fill the portfolio never records.
/// </para>
/// <para>
/// Every message is checked against the inbox store before handling, because
/// RabbitMQ guarantees at-least-once delivery and the handlers mutate financial
/// state. See <see cref="IInboxStore"/>.
/// </para>
/// </remarks>
public sealed class RabbitMqConsumerService(
    IRabbitMqConnectionProvider connectionProvider,
    IRabbitMqTopologyInitializer topologyInitializer,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    IEnumerable<SubscriptionDescriptor> subscriptions,
    ILogger<RabbitMqConsumerService> logger)
    : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Agentiva.Messaging.Consumer");

    private readonly RabbitMqOptions _options = options.Value;
    private readonly SubscriptionDescriptor[] _subscriptions = subscriptions.ToArray();
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriptions.Length == 0)
        {
            logger.LogInformation(
                "{ServiceName} subscribes to no events; the consumer will not start.", _options.ServiceName);
            return;
        }

        var routingKeys = _subscriptions.Select(s => s.RoutingKey).Distinct().ToArray();

        try
        {
            if (_options.DeclareTopologyOnStartup)
            {
                await topologyInitializer.InitializeAsync(routingKeys, stoppingToken);
            }

            var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
            _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            // Bound how many unacknowledged messages this consumer holds, so a
            // slow replica cannot starve a healthy one.
            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: _options.PrefetchCount,
                global: false,
                cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnMessageReceivedAsync;

            var queue = EventBusTopology.QueueFor(_options.ServiceName);

            await _channel.BasicConsumeAsync(
                queue: queue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            logger.LogInformation(
                "Consuming {Queue} with prefetch {Prefetch} for routing keys {RoutingKeys}",
                queue,
                _options.PrefetchCount,
                string.Join(", ", routingKeys));

            // Hold the background service open; delivery is callback-driven.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Consumer for {ServiceName} is shutting down.", _options.ServiceName);
        }
        catch (Exception ex)
        {
            // Rethrow so the host's unhandled-exception policy stops the service
            // and the orchestrator restarts it. A silently dead consumer is far
            // worse than a restarting pod: the queue would grow without anyone
            // noticing until the dashboard went stale.
            logger.LogCritical(ex, "Consumer for {ServiceName} failed fatally.", _options.ServiceName);
            throw;
        }
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        var headers = args.BasicProperties.Headers;

        // The retry hop rewrites the AMQP routing key to the delay tier, so the
        // original key — the one handlers are registered against — comes from a
        // header when present.
        var routingKey = ReadStringHeader(headers, EventBusTopology.Headers.OriginalRoutingKey)
                         ?? args.RoutingKey;

        var attempt = ReadIntHeader(headers, EventBusTopology.Headers.Attempt) ?? 1;
        var eventVersion = ReadIntHeader(headers, EventBusTopology.Headers.EventVersion) ?? 1;
        var correlationId = args.BasicProperties.CorrelationId
                            ?? ReadStringHeader(headers, EventBusTopology.Headers.CorrelationId)
                            ?? Guid.CreateVersion7().ToString();
        var agentRunId = ReadStringHeader(headers, EventBusTopology.Headers.AgentRunId);

        _ = Guid.TryParse(args.BasicProperties.MessageId, out var eventId);

        var payload = Encoding.UTF8.GetString(args.Body.Span);

        using var activity = ActivitySource.StartActivity($"consume {routingKey}", ActivityKind.Consumer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.rabbitmq.routing_key", routingKey);
        activity?.SetTag("messaging.message.id", eventId.ToString());
        activity?.SetTag("messaging.delivery_attempt", attempt);

        var context = new IntegrationEventContext(
            eventId, routingKey, correlationId, agentRunId, attempt, eventVersion);

        try
        {
            await DispatchAsync(routingKey, payload, context, CancellationToken.None);

            await _channel!.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await HandleFailureAsync(args, routingKey, attempt, correlationId, agentRunId, eventVersion, ex);
        }
    }

    private async Task DispatchAsync(
        string routingKey,
        string payload,
        IntegrationEventContext context,
        CancellationToken cancellationToken)
    {
        var matching = _subscriptions.Where(s => s.RoutingKey == routingKey).ToArray();

        if (matching.Length == 0)
        {
            // Bound to a key with no handler. Acknowledging is correct: the
            // message is not poison, the binding is simply broader than needed.
            logger.LogWarning(
                "Received {RoutingKey} but {ServiceName} has no handler registered for it; acknowledging.",
                routingKey,
                _options.ServiceName);
            return;
        }

        // A fresh DI scope per message, mirroring a request scope: each handler
        // gets its own DbContext and unit of work rather than sharing one across
        // the lifetime of the consumer.
        await using var scope = scopeFactory.CreateAsyncScope();

        if (scope.ServiceProvider.GetService<ICorrelationContext>() is CorrelationContext mutable)
        {
            mutable.CorrelationId = context.CorrelationId;
            mutable.RequestId = context.EventId.ToString();
            mutable.AgentRunId = context.AgentRunId;
        }

        var inbox = scope.ServiceProvider.GetService<IInboxStore>();

        foreach (var subscription in matching)
        {
            var handler = (IIntegrationEventHandler)scope.ServiceProvider.GetRequiredService(subscription.HandlerType);
            var consumerName = subscription.HandlerType.FullName ?? subscription.HandlerType.Name;

            if (inbox is not null && context.EventId != Guid.Empty)
            {
                if (await inbox.HasProcessedAsync(context.EventId, consumerName, cancellationToken))
                {
                    logger.LogDebug(
                        "Skipping {RoutingKey} event {EventId}: already processed by {Consumer}.",
                        routingKey,
                        context.EventId,
                        consumerName);
                    continue;
                }
            }

            await handler.HandleAsync(payload, context, cancellationToken);

            if (inbox is not null && context.EventId != Guid.Empty)
            {
                await inbox.MarkProcessedAsync(context.EventId, consumerName, routingKey, cancellationToken);
            }
        }
    }

    private async Task HandleFailureAsync(
        BasicDeliverEventArgs args,
        string routingKey,
        int attempt,
        string correlationId,
        string? agentRunId,
        int eventVersion,
        Exception exception)
    {
        var tiers = _options.RetryDelaysSeconds;
        var canRetry = attempt <= _options.MaxDeliveryAttempts && tiers.Length > 0;

        if (!canRetry)
        {
            logger.LogError(
                exception,
                "Dead-lettering {RoutingKey} after {Attempt} attempt(s) for correlation id {CorrelationId}. "
                + "The message is parked for operator inspection and will not be retried.",
                routingKey,
                attempt,
                correlationId);

            // Reject without requeue: the work queue's x-dead-letter-exchange
            // moves it to this service's parking queue.
            await _channel!.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        // Clamp so an attempt count beyond the configured tiers reuses the longest delay.
        var delaySeconds = tiers[Math.Min(attempt - 1, tiers.Length - 1)];

        logger.LogWarning(
            exception,
            "Handling {RoutingKey} failed on attempt {Attempt} for correlation id {CorrelationId}; "
            + "scheduling retry in {DelaySeconds}s.",
            routingKey,
            attempt,
            correlationId,
            delaySeconds);

        var properties = new BasicProperties
        {
            MessageId = args.BasicProperties.MessageId,
            CorrelationId = correlationId,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            Type = routingKey,
            Persistent = true,
            Headers = new Dictionary<string, object?>
            {
                [EventBusTopology.Headers.Attempt] = attempt + 1,
                [EventBusTopology.Headers.OriginalRoutingKey] = routingKey,
                [EventBusTopology.Headers.CorrelationId] = correlationId,
                [EventBusTopology.Headers.EventVersion] = eventVersion,

                // Truncated: a header carries the whole message through the
                // broker, and a long stack trace would bloat every retry.
                [EventBusTopology.Headers.LastError] = Truncate($"{exception.GetType().Name}: {exception.Message}", 512),
                [EventBusTopology.Headers.AgentRunId] = agentRunId
            }
        };

        await _channel!.BasicPublishAsync(
            exchange: EventBusTopology.RetryExchangeFor(_options.ServiceName),
            routingKey: EventBusTopology.RetryRoutingKeyFor(delaySeconds),
            mandatory: false,
            basicProperties: properties,
            body: args.Body.ToArray());

        // Acknowledge the original only after the retry copy is safely published,
        // so a failure to schedule the retry leaves the message on the queue
        // rather than dropping it.
        await _channel.BasicAckAsync(args.DeliveryTag, multiple: false);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string? ReadStringHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        // RabbitMQ delivers long-string header values as byte arrays.
        return raw switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => raw.ToString()
        };
    }

    private static int? ReadIntHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancel the consumer first so no new deliveries arrive, then let the
        // base implementation unwind. In-flight handlers finish and acknowledge.
        if (_channel is { IsOpen: true })
        {
            try
            {
                await _channel.CloseAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error closing the consumer channel during shutdown.");
            }
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}

/// <summary>Binds a routing key to the handler type that processes it.</summary>
/// <param name="RoutingKey">Routing key from <c>EventTypes</c>.</param>
/// <param name="HandlerType">Concrete handler type, resolved per message from a fresh scope.</param>
public sealed record SubscriptionDescriptor(string RoutingKey, Type HandlerType);
