namespace Agentiva.BuildingBlocks.Messaging.Configuration;

/// <summary>
/// Names and shape of the Agentiva RabbitMQ topology.
/// </summary>
/// <remarks>
/// <para>
/// The happy path is a plain topic exchange fan-out:
/// </para>
/// <code>
///   publisher -> agentiva.events (topic) --[order.filled]--> agentiva.{service}.q -> ack
/// </code>
/// <para>
/// Retries are where the design earns its complexity. A failed message must wait
/// before its next attempt, and it must come back to <em>only</em> the service
/// that failed. Those two requirements together rule out the obvious approach of
/// dead-lettering a shared delay queue onto the main exchange: the main exchange
/// is a topic exchange, so the message would fan back out to every service bound
/// to that routing key, and a transient fault in one consumer would redeliver the
/// event to all its peers.
/// </para>
/// <para>
/// So the retry loop is private to each service:
/// </para>
/// <code>
///   agentiva.{service}.q
///        |  handler threw, attempts remaining
///        v
///   agentiva.{service}.retry.x        (topic, routing key "{delay}s")
///        |
///        v
///   agentiva.{service}.retry.{delay}s.q
///        |    x-message-ttl        = delay
///        |    x-dead-letter-exchange = agentiva.{service}.requeue.x
///        |  ... message sits here for the delay ...
///        v
///   agentiva.{service}.requeue.x      (fanout, bound only to this service)
///        |
///        v
///   agentiva.{service}.q              (next attempt)
/// </code>
/// <para>
/// A fanout requeue exchange bound to exactly one queue is what keeps the retry
/// private. The AMQP routing key is rewritten to <c>{delay}s</c> by the retry
/// hop, so the original key travels in the
/// <see cref="Headers.OriginalRoutingKey"/> header and the consumer dispatches on
/// that rather than on the delivery's routing key.
/// </para>
/// <para>
/// Waiting is done with a message TTL rather than a <c>Task.Delay</c> in the
/// consumer. An in-process delay holds an unacknowledged message, blocks a
/// prefetch slot, and is lost outright if the pod restarts; a TTL queue survives
/// a broker failover and costs the consumer nothing while it waits.
/// </para>
/// <para>
/// Once attempts are exhausted the message is rejected without requeue and the
/// work queue's own dead-letter exchange parks it. Nothing is ever dropped
/// automatically: a parked message is evidence, and in a financial system
/// discarding evidence is worse than a full queue.
/// </para>
/// </remarks>
public static class EventBusTopology
{
    /// <summary>Primary topic exchange carrying all domain events.</summary>
    public const string MainExchange = "agentiva.events";

    /// <summary>Exchange that receives messages which exhausted their retries.</summary>
    public const string DeadLetterExchange = "agentiva.events.dlx";

    /// <summary>Queue draining events for one service, e.g. <c>agentiva.portfolio-service.q</c>.</summary>
    public static string QueueFor(string serviceName) => $"agentiva.{serviceName}.q";

    /// <summary>Parking queue for a service's exhausted messages.</summary>
    public static string DeadLetterQueueFor(string serviceName) => $"agentiva.{serviceName}.dlq";

    /// <summary>Per-service topic exchange that feeds that service's delay queues.</summary>
    public static string RetryExchangeFor(string serviceName) => $"agentiva.{serviceName}.retry.x";

    /// <summary>
    /// Per-service fanout exchange that returns an expired delayed message to
    /// that service's work queue, and to no other.
    /// </summary>
    public static string RequeueExchangeFor(string serviceName) => $"agentiva.{serviceName}.requeue.x";

    /// <summary>Per-service delay queue for one backoff tier.</summary>
    public static string RetryQueueFor(string serviceName, int delaySeconds)
        => $"agentiva.{serviceName}.retry.{delaySeconds}s.q";

    /// <summary>Routing key a message carries while waiting in a delay tier.</summary>
    public static string RetryRoutingKeyFor(int delaySeconds) => $"{delaySeconds}s";

    /// <summary>Message headers Agentiva adds for correlation and retry accounting.</summary>
    public static class Headers
    {
        /// <summary>Delivery attempts made so far. Selects the backoff tier.</summary>
        public const string Attempt = "x-agentiva-attempt";

        /// <summary>
        /// The routing key the event was originally published under. Required:
        /// the retry hop overwrites the AMQP routing key with the delay tier.
        /// </summary>
        public const string OriginalRoutingKey = "x-agentiva-original-routing-key";

        /// <summary>Why the last attempt failed. Read on the dead-letter queue.</summary>
        public const string LastError = "x-agentiva-last-error";

        /// <summary>End-to-end correlation identifier.</summary>
        public const string CorrelationId = "x-agentiva-correlation-id";

        /// <summary>Originating AI agent run, when applicable.</summary>
        public const string AgentRunId = "x-agentiva-agent-run-id";

        /// <summary>W3C trace context, so a trace spans the broker hop.</summary>
        public const string TraceParent = "traceparent";

        /// <summary>Payload schema version.</summary>
        public const string EventVersion = "x-agentiva-event-version";
    }
}
