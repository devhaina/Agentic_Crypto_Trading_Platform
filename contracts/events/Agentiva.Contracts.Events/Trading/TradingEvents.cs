namespace Agentiva.Contracts.Events.Trading;

/// <summary>
/// A trading intent has been recorded and is entering the risk gate.
/// </summary>
/// <remarks>
/// An intent is the platform's unit of "we would like to trade". It exists
/// before any risk decision and before any order, which is what makes a
/// rejection auditable: the record of wanting to trade survives the rejection.
/// </remarks>
public sealed record TradeIntentCreated : IntegrationEvent
{
    public override string EventType => EventTypes.Trading.IntentCreated;

    public required Guid TradingIntentId { get; init; }

    public required Guid TradingAccountId { get; init; }

    public required string Symbol { get; init; }

    /// <summary><c>BUY</c> or <c>SELL</c>.</summary>
    public required string Side { get; init; }

    /// <summary>Requested base-asset quantity, before risk-driven reduction.</summary>
    public required decimal RequestedQuantity { get; init; }

    public required decimal EntryPrice { get; init; }

    public decimal? StopLoss { get; init; }

    public decimal? TakeProfit { get; init; }

    /// <summary>Originating deterministic signal, when there was one.</summary>
    public Guid? SignalId { get; init; }

    /// <summary>Originating strategy, when there was one.</summary>
    public Guid? StrategyId { get; init; }

    /// <summary>What created this intent: <c>STRATEGY</c>, <c>AGENT</c> or <c>MANUAL</c>.</summary>
    public required string Source { get; init; }

    /// <summary>Effective trading mode: <c>BACKTEST</c>, <c>PAPER</c> or <c>LIVE</c>.</summary>
    public required string TradingMode { get; init; }

    /// <summary>Idempotency key that guards the whole intent-to-order path.</summary>
    public required string IdempotencyKey { get; init; }
}

/// <summary>
/// New order admission has been disabled platform-wide.
/// </summary>
/// <remarks>
/// Deliberately coarse. On any integrity doubt the platform stops opening new
/// risk rather than attempting a clever partial degradation. Existing positions
/// are left untouched; forced liquidation is never automatic.
/// </remarks>
public sealed record TradingDisabled : IntegrationEvent
{
    public override string EventType => EventTypes.Trading.Disabled;

    /// <summary>Stable reason code, e.g. <c>reconciliation.failed</c>.</summary>
    public required string Reason { get; init; }

    public required string Detail { get; init; }

    /// <summary>What disabled trading: <c>SYSTEM</c> or an operator's user id.</summary>
    public required string TriggeredBy { get; init; }

    /// <summary>
    /// True when re-enabling requires a human. Automated triggers set this so
    /// that a transient fault cannot silently resume live trading.
    /// </summary>
    public required bool RequiresManualReset { get; init; }
}

/// <summary>New order admission has been restored.</summary>
public sealed record TradingEnabled : IntegrationEvent
{
    public override string EventType => EventTypes.Trading.Enabled;

    /// <summary>User id of the operator who re-enabled trading. Never <c>SYSTEM</c>.</summary>
    public required string EnabledBy { get; init; }

    public required string Justification { get; init; }
}
