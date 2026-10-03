using Agentiva.BuildingBlocks.Messaging.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Agentiva.BuildingBlocks.Messaging.RabbitMq;

/// <summary>Declares the exchanges, queues and bindings this service needs.</summary>
public interface IRabbitMqTopologyInitializer
{
    /// <summary>
    /// Declares the topology. Idempotent: an AMQP declaration succeeds silently
    /// when the entity already exists with identical arguments, so every replica
    /// can run this at startup.
    /// </summary>
    /// <param name="subscribedRoutingKeys">Routing keys this service binds its work queue to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InitializeAsync(IReadOnlyCollection<string> subscribedRoutingKeys, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IRabbitMqTopologyInitializer"/>
public sealed class RabbitMqTopologyInitializer(
    IRabbitMqConnectionProvider connectionProvider,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqTopologyInitializer> logger)
    : IRabbitMqTopologyInitializer
{
    private readonly RabbitMqOptions _options = options.Value;

    public async Task InitializeAsync(
        IReadOnlyCollection<string> subscribedRoutingKeys,
        CancellationToken cancellationToken)
    {
        var service = _options.ServiceName;
        var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var classicQueue = new Dictionary<string, object?> { ["x-queue-type"] = "classic" };

        // --- Shared exchanges -----------------------------------------------
        await channel.ExchangeDeclareAsync(
            EventBusTopology.MainExchange, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            EventBusTopology.DeadLetterExchange, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);

        // --- Per-service retry plumbing --------------------------------------
        var retryExchange = EventBusTopology.RetryExchangeFor(service);
        var requeueExchange = EventBusTopology.RequeueExchangeFor(service);

        await channel.ExchangeDeclareAsync(
            retryExchange, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);

        // Fanout, and bound to exactly one queue. This is what confines a retry
        // to the service that failed instead of fanning it back out to peers.
        await channel.ExchangeDeclareAsync(
            requeueExchange, ExchangeType.Fanout,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);

        // --- Work queue -------------------------------------------------------
        var queue = EventBusTopology.QueueFor(service);
        var deadLetterQueue = EventBusTopology.DeadLetterQueueFor(service);

        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = EventBusTopology.DeadLetterExchange,
                ["x-queue-type"] = "classic"
            },
            cancellationToken: cancellationToken);

        foreach (var routingKey in subscribedRoutingKeys)
        {
            await channel.QueueBindAsync(
                queue, EventBusTopology.MainExchange, routingKey, cancellationToken: cancellationToken);
        }

        // Expired delayed messages come back here.
        await channel.QueueBindAsync(
            queue, requeueExchange, routingKey: string.Empty, cancellationToken: cancellationToken);

        // --- Delay queues, one per backoff tier -------------------------------
        foreach (var delaySeconds in _options.RetryDelaysSeconds.Distinct())
        {
            var retryQueue = EventBusTopology.RetryQueueFor(service, delaySeconds);

            await channel.QueueDeclareAsync(
                retryQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = delaySeconds * 1000,
                    ["x-dead-letter-exchange"] = requeueExchange,
                    ["x-queue-type"] = "classic"
                },
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                retryQueue,
                retryExchange,
                EventBusTopology.RetryRoutingKeyFor(delaySeconds),
                cancellationToken: cancellationToken);
        }

        // --- Dead-letter parking queue ----------------------------------------
        await channel.QueueDeclareAsync(
            deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: classicQueue,
            cancellationToken: cancellationToken);

        // Bind only to the keys this service consumes. Binding "#" would park
        // every service's failures in every service's dead-letter queue.
        foreach (var routingKey in subscribedRoutingKeys)
        {
            await channel.QueueBindAsync(
                deadLetterQueue,
                EventBusTopology.DeadLetterExchange,
                routingKey,
                cancellationToken: cancellationToken);
        }

        logger.LogInformation(
            "Declared RabbitMQ topology for {ServiceName}: work queue {Queue} bound to {BindingCount} routing "
            + "key(s), retry tiers {RetryTiers}s, dead-letter queue {DeadLetterQueue}",
            service,
            queue,
            subscribedRoutingKeys.Count,
            string.Join("/", _options.RetryDelaysSeconds),
            deadLetterQueue);
    }
}
