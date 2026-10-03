namespace Agentiva.BuildingBlocks.Messaging.Abstractions;

/// <summary>
/// Records which events a service has already processed, so that at-least-once
/// delivery becomes effectively-once processing.
/// </summary>
/// <remarks>
/// <para>
/// Needed because RabbitMQ redelivers. If a consumer applies a fill to a
/// position, then crashes before acknowledging, the broker hands the same fill
/// to the next replica — and applying it twice doubles the position. Recording
/// the event id in the same transaction as the state change makes the second
/// application a no-op.
/// </para>
/// <para>
/// Durable storage, not Redis: an evicted key here means a double-applied fill.
/// </para>
/// </remarks>
public interface IInboxStore
{
    /// <summary>Whether this event has already been processed to completion.</summary>
    Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken);

    /// <summary>
    /// Records an event as processed. Must be called in the same transaction as
    /// the state change it guards, or the guarantee is lost.
    /// </summary>
    Task MarkProcessedAsync(
        Guid eventId,
        string consumerName,
        string eventType,
        CancellationToken cancellationToken);
}
