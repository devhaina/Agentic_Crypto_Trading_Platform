namespace Agentiva.Contracts.Events;

/// <summary>
/// Every routing key published on the Agentiva event bus.
/// </summary>
/// <remarks>
/// <para>
/// These strings are a published contract. A consumer in another service — or
/// another language — binds a queue to them, so renaming one in place silently
/// stops delivery rather than failing a build. To change an event's shape,
/// publish a new version alongside the old (see <c>IntegrationEvent.Version</c>)
/// and retire the old one once every consumer has moved.
/// </para>
/// <para>
/// Naming follows <c>{aggregate}.{entity}.{past-tense-verb}</c> so that
/// wildcard bindings are useful: <c>market.#</c> captures all market data,
/// <c>order.*</c> all order lifecycle transitions.
/// </para>
/// </remarks>
public static class EventTypes
{
    /// <summary>Market data events, published by the Market Data Service.</summary>
    public static class Market
    {
        public const string TickCreated = "market.tick.created";
        public const string TradeCreated = "market.trade.created";
        public const string CandleCreated = "market.candle.created";
        public const string OrderBookUpdated = "market.orderbook.updated";

        /// <summary>
        /// Market data has aged past its staleness threshold. A risk-relevant
        /// event: trading on stale prices is how a system sizes a position
        /// against a market that has already moved.
        /// </summary>
        public const string DataStale = "market.data.stale";
    }

    /// <summary>Deterministic strategy engine events.</summary>
    public static class Strategy
    {
        public const string SignalCreated = "signal.created";
    }

    /// <summary>AI agent platform events. Advisory only — these never authorise execution.</summary>
    public static class Agents
    {
        public const string AnalysisCompleted = "agent.analysis.completed";
        public const string SignalProposed = "agent.signal.proposed";
    }

    /// <summary>Trading workflow events.</summary>
    public static class Trading
    {
        public const string IntentCreated = "trade.intent.created";

        /// <summary>New order admission has been disabled platform-wide.</summary>
        public const string Disabled = "trading.disabled";

        /// <summary>New order admission has been restored after an operator review.</summary>
        public const string Enabled = "trading.enabled";
    }

    /// <summary>Deterministic risk gate outcomes.</summary>
    public static class Risk
    {
        public const string Approved = "risk.approved";
        public const string Rejected = "risk.rejected";
        public const string LimitBreached = "risk.limit.breached";
    }

    /// <summary>Order lifecycle events, published by the Execution Service.</summary>
    public static class Orders
    {
        public const string Created = "order.created";
        public const string Submitted = "order.submitted";
        public const string PartiallyFilled = "order.partially.filled";
        public const string Filled = "order.filled";
        public const string Cancelled = "order.cancelled";
        public const string Rejected = "order.rejected";

        /// <summary>
        /// The exchange outcome could not be established. Blocks automated
        /// trading on the symbol until reconciled.
        /// </summary>
        public const string Indeterminate = "order.indeterminate";
    }

    /// <summary>Portfolio state events.</summary>
    public static class Portfolio
    {
        public const string PositionUpdated = "position.updated";
        public const string PortfolioUpdated = "portfolio.updated";
        public const string PnlUpdated = "pnl.updated";
        public const string BalanceUpdated = "balance.updated";
        public const string TradeCompleted = "trade.completed";
    }

    /// <summary>Operational and integrity events.</summary>
    public static class Operations
    {
        public const string ReconciliationFailed = "reconciliation.failed";
        public const string ReconciliationSucceeded = "reconciliation.succeeded";
        public const string KillSwitchActivated = "killswitch.activated";
        public const string KillSwitchDeactivated = "killswitch.deactivated";
        public const string AlertRaised = "alert.raised";
        public const string AuditEventRecorded = "audit.event.recorded";
    }

    /// <summary>All routing keys, for topology declaration and contract tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Market.TickCreated,
        Market.TradeCreated,
        Market.CandleCreated,
        Market.OrderBookUpdated,
        Market.DataStale,
        Strategy.SignalCreated,
        Agents.AnalysisCompleted,
        Agents.SignalProposed,
        Trading.IntentCreated,
        Trading.Disabled,
        Trading.Enabled,
        Risk.Approved,
        Risk.Rejected,
        Risk.LimitBreached,
        Orders.Created,
        Orders.Submitted,
        Orders.PartiallyFilled,
        Orders.Filled,
        Orders.Cancelled,
        Orders.Rejected,
        Orders.Indeterminate,
        Portfolio.PositionUpdated,
        Portfolio.PortfolioUpdated,
        Portfolio.PnlUpdated,
        Portfolio.BalanceUpdated,
        Portfolio.TradeCompleted,
        Operations.ReconciliationFailed,
        Operations.ReconciliationSucceeded,
        Operations.KillSwitchActivated,
        Operations.KillSwitchDeactivated,
        Operations.AlertRaised,
        Operations.AuditEventRecorded
    ];
}
