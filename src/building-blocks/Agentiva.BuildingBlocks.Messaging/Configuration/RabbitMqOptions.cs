using System.ComponentModel.DataAnnotations;

namespace Agentiva.BuildingBlocks.Messaging.Configuration;

/// <summary>Connection and consumer settings for the RabbitMQ event bus.</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string Host { get; set; } = "rabbitmq";

    [Range(1, 65535)]
    public int Port { get; set; } = 5672;

    [Required]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Broker password. Supplied from the environment or a secret manager and
    /// never written to configuration files or logs.
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Identifies this service to the broker, so the management UI shows which
    /// service owns a connection.
    /// </summary>
    [Required]
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Unacknowledged messages allowed per consumer.
    /// </summary>
    /// <remarks>
    /// Kept low on purpose. A high prefetch lets one slow consumer hoard
    /// messages that a healthy replica could have processed, and on a crash all
    /// of them are redelivered at once.
    /// </remarks>
    [Range(1, 1000)]
    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>
    /// Delivery attempts before a message is dead-lettered. Each attempt waits
    /// longer than the last; see <see cref="RetryDelaysSeconds"/>.
    /// </summary>
    [Range(0, 10)]
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>
    /// Backoff tiers in seconds. One durable retry queue is declared per tier,
    /// each with a message TTL that dead-letters back onto the main exchange.
    /// </summary>
    public int[] RetryDelaysSeconds { get; set; } = [5, 30, 120];

    /// <summary>Whether to declare exchanges and queues at startup.</summary>
    /// <remarks>
    /// On in development so a fresh <c>docker compose up</c> works unattended.
    /// In production the topology is applied as a reviewed migration step and
    /// services run with this off, so a misconfigured replica cannot silently
    /// create a queue that nothing drains.
    /// </remarks>
    public bool DeclareTopologyOnStartup { get; set; } = true;

    /// <summary>Seconds to wait between connection attempts at startup.</summary>
    [Range(1, 300)]
    public int ConnectionRetryDelaySeconds { get; set; } = 5;

    /// <summary>Connection attempts at startup before the service reports unhealthy.</summary>
    [Range(1, 100)]
    public int ConnectionRetryAttempts { get; set; } = 12;

    /// <summary>Builds the AMQP URI. Never logged: it embeds the password.</summary>
    public Uri BuildUri()
        => new($"amqp://{Uri.EscapeDataString(UserName)}:{Uri.EscapeDataString(Password)}@{Host}:{Port}/{Uri.EscapeDataString(VirtualHost == "/" ? string.Empty : VirtualHost)}");

    /// <summary>Connection description safe to log, with the password removed.</summary>
    public string ToSafeString() => $"amqp://{UserName}@{Host}:{Port}/{VirtualHost}";
}
