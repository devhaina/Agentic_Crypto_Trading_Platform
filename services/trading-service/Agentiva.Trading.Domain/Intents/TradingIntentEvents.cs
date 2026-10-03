using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.Contracts.Events;

namespace Agentiva.Trading.Domain.Intents;

/// <summary>Raised when a trading intent is recorded.</summary>
/// <remarks>
/// Written to the outbox in the same transaction as the intent itself, so the
/// intent and the announcement of it cannot diverge.
/// </remarks>
public sealed record TradeIntentCreatedDomainEvent : DomainEvent
{
    public TradeIntentCreatedDomainEvent(
        DateTimeOffset occurredAt,
        Guid tradingIntentId,
        Guid tradingAccountId,
        string symbol,
        string side,
        decimal requestedQuantity,
        decimal entryPrice,
        decimal? stopLoss,
        decimal? takeProfit,
        Guid? signalId,
        Guid? strategyId,
        string source,
        string tradingMode,
        string idempotencyKey)
        : base(occurredAt)
    {
        TradingIntentId = tradingIntentId;
        TradingAccountId = tradingAccountId;
        Symbol = symbol;
        Side = side;
        RequestedQuantity = requestedQuantity;
        EntryPrice = entryPrice;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        SignalId = signalId;
        StrategyId = strategyId;
        Source = source;
        TradingMode = tradingMode;
        IdempotencyKey = idempotencyKey;
    }

    public override string EventType => EventTypes.Trading.IntentCreated;

    public Guid TradingIntentId { get; }

    public Guid TradingAccountId { get; }

    public string Symbol { get; }

    public string Side { get; }

    public decimal RequestedQuantity { get; }

    public decimal EntryPrice { get; }

    public decimal? StopLoss { get; }

    public decimal? TakeProfit { get; }

    public Guid? SignalId { get; }

    public Guid? StrategyId { get; }

    public string Source { get; }

    public string TradingMode { get; }

    public string IdempotencyKey { get; }
}
