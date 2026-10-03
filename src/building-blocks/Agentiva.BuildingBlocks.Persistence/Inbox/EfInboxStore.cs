using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.BuildingBlocks.Persistence.Inbox;

/// <summary>EF Core implementation of <see cref="IInboxStore"/>.</summary>
/// <typeparam name="TContext">The service's database context.</typeparam>
/// <remarks>
/// <see cref="MarkProcessedAsync"/> only adds to the change tracker; the handler's
/// own <c>SaveChangesAsync</c> is what commits it. Keeping the mark in the
/// handler's transaction is the entire guarantee: committing it separately would
/// leave a window where the state change is committed and the mark is not, and a
/// redelivery would then apply the change twice.
/// </remarks>
public sealed class EfInboxStore<TContext>(TContext context, IClock clock) : IInboxStore
    where TContext : AgentivaDbContext
{
    public Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken)
        => context.InboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.EventId == eventId && m.ConsumerName == consumerName, cancellationToken);

    public Task MarkProcessedAsync(
        Guid eventId,
        string consumerName,
        string eventType,
        CancellationToken cancellationToken)
    {
        context.InboxMessages.Add(new InboxMessage
        {
            EventId = eventId,
            ConsumerName = consumerName,
            EventType = eventType,
            ProcessedAt = clock.UtcNow
        });

        return Task.CompletedTask;
    }
}
