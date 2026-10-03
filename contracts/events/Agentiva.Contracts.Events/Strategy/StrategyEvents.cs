namespace Agentiva.Contracts.Events.Strategy;

/// <summary>
/// A deterministic strategy has produced a trading signal.
/// </summary>
/// <remarks>
/// A signal is a recommendation, not an instruction. It must still pass the
/// Trading Service workflow and the deterministic risk gate before any order
/// exists. The Strategy Service never contacts the Execution Service.
/// </remarks>
public sealed record SignalCreated : IntegrationEvent
{
    public override string EventType => EventTypes.Strategy.SignalCreated;

    public required Guid SignalId { get; init; }

    public required string Symbol { get; init; }

    /// <summary>One of <c>BUY</c>, <c>SELL</c> or <c>HOLD</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Strategy confidence in the range 0 to 1 inclusive.</summary>
    public required decimal Confidence { get; init; }

    public required decimal EntryPrice { get; init; }

    /// <summary>
    /// Protective stop. Required by default risk policy: without it, position
    /// size cannot be derived from a bounded risk amount.
    /// </summary>
    public decimal? StopLoss { get; init; }

    public decimal? TakeProfit { get; init; }

    public required Guid StrategyId { get; init; }

    /// <summary>Human-readable strategy name, e.g. <c>EMA_RSI</c>.</summary>
    public required string StrategyName { get; init; }

    /// <summary>Semantic version of the strategy that produced this signal, e.g. <c>1.0.0</c>.</summary>
    public required string StrategyVersion { get; init; }

    /// <summary>Candle interval the signal was computed on.</summary>
    public required string Timeframe { get; init; }

    /// <summary>
    /// Stable codes explaining the signal, e.g. <c>EMA_CROSS_UP</c>. Carried
    /// through to the audit record so a trade can be explained after the fact.
    /// </summary>
    public required IReadOnlyList<string> ReasonCodes { get; init; }

    /// <summary>Close time of the candle the signal was computed from.</summary>
    public required DateTimeOffset ComputedAt { get; init; }
}
