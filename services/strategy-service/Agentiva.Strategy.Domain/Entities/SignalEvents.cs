using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.Contracts.Events;

namespace Agentiva.Strategy.Domain.Entities;

/// <summary>
/// Raised when a signal is recorded for an actionable (buy or sell) decision.
/// </summary>
/// <remarks>
/// <para>
/// Written to the outbox in the same transaction as the <see cref="Signal"/>
/// row itself, so the record and the announcement of it cannot diverge.
/// </para>
/// <para>
/// Mirrors the shape of <c>Agentiva.Contracts.Events.Strategy.SignalCreated</c>
/// field for field — see the remarks on <c>TradeIntentCreatedDomainEvent</c> in
/// the Trading Service for why the domain layer defines its own event type
/// rather than depending on the published contract class directly.
/// </para>
/// </remarks>
public sealed record SignalCreatedDomainEvent : DomainEvent
{
    public SignalCreatedDomainEvent(
        DateTimeOffset occurredAt,
        Guid signalId,
        string symbol,
        string action,
        decimal confidence,
        decimal entryPrice,
        decimal? stopLoss,
        decimal? takeProfit,
        Guid strategyId,
        string strategyName,
        string strategyVersion,
        string timeframe,
        IReadOnlyList<string> reasonCodes,
        DateTimeOffset computedAt)
        : base(occurredAt)
    {
        SignalId = signalId;
        Symbol = symbol;
        Action = action;
        Confidence = confidence;
        EntryPrice = entryPrice;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        StrategyId = strategyId;
        StrategyName = strategyName;
        StrategyVersion = strategyVersion;
        Timeframe = timeframe;
        ReasonCodes = reasonCodes;
        ComputedAt = computedAt;
    }

    public override string EventType => EventTypes.Strategy.SignalCreated;

    public Guid SignalId { get; }

    public string Symbol { get; }

    public string Action { get; }

    public decimal Confidence { get; }

    public decimal EntryPrice { get; }

    public decimal? StopLoss { get; }

    public decimal? TakeProfit { get; }

    public Guid StrategyId { get; }

    public string StrategyName { get; }

    public string StrategyVersion { get; }

    public string Timeframe { get; }

    public IReadOnlyList<string> ReasonCodes { get; }

    public DateTimeOffset ComputedAt { get; }
}
