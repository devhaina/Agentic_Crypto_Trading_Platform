using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.Contracts.Events;

namespace Agentiva.Portfolio.Domain.Positions;

/// <summary>
/// Raised whenever a fill changes a position's size, average entry or P&amp;L.
/// </summary>
/// <remarks>
/// Field names mirror <see cref="Contracts.Events.Portfolio.PositionUpdated"/>
/// exactly — the outbox publishes this event's own serialised JSON under that
/// event's routing key, so the two shapes must stay in lock-step even though
/// they are different CLR types. See the equivalent remark on
/// <c>Execution.Domain.Orders.OrderDomainEvents</c>.
/// </remarks>
public sealed record PositionUpdatedDomainEvent : DomainEvent
{
    public PositionUpdatedDomainEvent(
        DateTimeOffset occurredAt,
        Guid positionId,
        Guid tradingAccountId,
        string symbol,
        string direction,
        decimal quantity,
        decimal averageEntryPrice,
        decimal realizedPnl,
        decimal unrealizedPnl,
        decimal markPrice,
        string quoteAsset)
        : base(occurredAt)
    {
        PositionId = positionId;
        TradingAccountId = tradingAccountId;
        Symbol = symbol;
        Direction = direction;
        Quantity = quantity;
        AverageEntryPrice = averageEntryPrice;
        RealizedPnl = realizedPnl;
        UnrealizedPnl = unrealizedPnl;
        MarkPrice = markPrice;
        QuoteAsset = quoteAsset;
    }

    public override string EventType => EventTypes.Portfolio.PositionUpdated;

    public Guid PositionId { get; }

    public Guid TradingAccountId { get; }

    public string Symbol { get; }

    public string Direction { get; }

    public decimal Quantity { get; }

    public decimal AverageEntryPrice { get; }

    public decimal RealizedPnl { get; }

    public decimal UnrealizedPnl { get; }

    public decimal MarkPrice { get; }

    public string QuoteAsset { get; }
}

/// <summary>Raised when a position's round trip closes completely and its realised P&amp;L is final.</summary>
public sealed record TradeCompletedDomainEvent : DomainEvent
{
    public TradeCompletedDomainEvent(
        DateTimeOffset occurredAt,
        Guid tradeId,
        Guid tradingAccountId,
        string symbol,
        string side,
        decimal entryPrice,
        decimal exitPrice,
        decimal quantity,
        decimal realizedPnl,
        decimal totalFees,
        DateTimeOffset openedAt,
        DateTimeOffset closedAt,
        string quoteAsset)
        : base(occurredAt)
    {
        TradeId = tradeId;
        TradingAccountId = tradingAccountId;
        Symbol = symbol;
        Side = side;
        EntryPrice = entryPrice;
        ExitPrice = exitPrice;
        Quantity = quantity;
        RealizedPnl = realizedPnl;
        TotalFees = totalFees;
        OpenedAt = openedAt;
        ClosedAt = closedAt;
        QuoteAsset = quoteAsset;
    }

    public override string EventType => EventTypes.Portfolio.TradeCompleted;

    public Guid TradeId { get; }

    public Guid TradingAccountId { get; }

    public string Symbol { get; }

    /// <summary>The side that opened the round trip — <c>BUY</c> for a long, <c>SELL</c> for a short.</summary>
    public string Side { get; }

    public decimal EntryPrice { get; }

    public decimal ExitPrice { get; }

    public decimal Quantity { get; }

    public decimal RealizedPnl { get; }

    public decimal TotalFees { get; }

    public DateTimeOffset OpenedAt { get; }

    public DateTimeOffset ClosedAt { get; }

    /// <summary>
    /// Always null: this event has no path to the signal or strategy that
    /// originated the trading intent, since <c>OrderFilled</c> does not carry
    /// one. See docs/architecture/known-limitations.md.
    /// </summary>
    public Guid? SignalId { get; }

    public Guid? StrategyId { get; }

    public string QuoteAsset { get; }
}
