namespace Agentiva.BuildingBlocks.Domain.Primitives;

/// <summary>Direction of an order. Quantities stay unsigned; this carries intent.</summary>
public enum OrderSide
{
    Buy = 1,
    Sell = 2
}

/// <summary>Supported exchange order types. MVP scope is market and limit only.</summary>
public enum OrderType
{
    Market = 1,
    Limit = 2,
    StopLoss = 3,
    StopLossLimit = 4,
    TakeProfit = 5,
    TakeProfitLimit = 6
}

/// <summary>How long an order remains active on the exchange book.</summary>
public enum TimeInForce
{
    /// <summary>Good till cancelled.</summary>
    GoodTillCancel = 1,

    /// <summary>Immediate or cancel.</summary>
    ImmediateOrCancel = 2,

    /// <summary>Fill or kill.</summary>
    FillOrKill = 3
}

/// <summary>
/// Execution mode. Enforced at the execution boundary, not merely in configuration.
/// </summary>
/// <remarks>
/// <see cref="Live"/> is the only value that reaches a real exchange with real
/// funds, and it additionally requires an explicit allow-live flag. Development
/// and test environments default to <see cref="Paper"/>.
/// </remarks>
public enum TradingMode
{
    /// <summary>Historical simulation. No exchange contact whatsoever.</summary>
    Backtest = 1,

    /// <summary>Live market data, simulated fills. No exchange orders.</summary>
    Paper = 2,

    /// <summary>Real orders against real funds.</summary>
    Live = 3
}

/// <summary>Lifecycle state of an order, mirroring the exchange state machine.</summary>
public enum OrderStatus
{
    /// <summary>Persisted locally, not yet sent to the exchange.</summary>
    Created = 1,

    /// <summary>Handed to the exchange; awaiting acknowledgement.</summary>
    Submitted = 2,

    /// <summary>Acknowledged and resting on the book.</summary>
    Accepted = 3,

    PartiallyFilled = 4,
    Filled = 5,
    Cancelled = 6,
    Rejected = 7,
    Expired = 8,

    /// <summary>
    /// Terminal-unknown: the exchange outcome could not be established.
    /// Requires operator reconciliation and blocks further automated trading
    /// on the symbol, because the true position is unknown.
    /// </summary>
    Unknown = 9
}

/// <summary>The action a signal or proposal recommends.</summary>
public enum TradeAction
{
    Buy = 1,
    Sell = 2,

    /// <summary>Explicitly take no action. Modelled so that a decision to stand aside is auditable.</summary>
    Hold = 3
}

/// <summary>Outcome of a deterministic risk evaluation.</summary>
public enum RiskDecision
{
    Approved = 1,
    Rejected = 2
}
