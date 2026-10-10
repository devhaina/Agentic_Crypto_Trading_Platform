using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.Contracts.Events;

namespace Agentiva.Portfolio.Domain.Accounts;

/// <summary>Raised whenever a fill changes an account's quote-asset cash balance.</summary>
/// <remarks>Field names mirror <see cref="Contracts.Events.Portfolio.BalanceUpdated"/> exactly.</remarks>
public sealed record BalanceUpdatedDomainEvent : DomainEvent
{
    public BalanceUpdatedDomainEvent(
        DateTimeOffset occurredAt, Guid tradingAccountId, string asset, decimal free, decimal locked)
        : base(occurredAt)
    {
        TradingAccountId = tradingAccountId;
        Asset = asset;
        Free = free;
        Locked = locked;
    }

    public override string EventType => EventTypes.Portfolio.BalanceUpdated;

    public Guid TradingAccountId { get; }

    public string Asset { get; }

    public decimal Free { get; }

    public decimal Locked { get; }
}
