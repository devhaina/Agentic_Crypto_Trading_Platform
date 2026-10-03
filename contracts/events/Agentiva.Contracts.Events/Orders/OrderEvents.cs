namespace Agentiva.Contracts.Events.Orders;

/// <summary>An order record has been created locally, before exchange submission.</summary>
/// <remarks>
/// Persisting the order before submitting it is what makes recovery possible: if
/// the process dies between creation and submission, the order exists in a known
/// state rather than being lost or duplicated.
/// </remarks>
public sealed record OrderCreated : IntegrationEvent
{
    public override string EventType => EventTypes.Orders.Created;

    public required Guid OrderId { get; init; }

    public required Guid TradingIntentId { get; init; }

    public required Guid RiskCheckId { get; init; }

    public required string Symbol { get; init; }

    public required string Side { get; init; }

    /// <summary><c>MARKET</c>, <c>LIMIT</c> and so on.</summary>
    public required string OrderType { get; init; }

    public required decimal Quantity { get; init; }

    /// <summary>Limit price. Null for a market order.</summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// Deterministic client order id sent to the exchange, e.g.
    /// <c>AGENTIVA-BTCUSDT-20261003-000001</c>. The exchange rejects a duplicate,
    /// which makes it the last line of defence against a double order.
    /// </summary>
    public required string ClientOrderId { get; init; }

    public required string TradingMode { get; init; }
}

/// <summary>The order has been handed to the exchange and acknowledged.</summary>
public sealed record OrderSubmitted : IntegrationEvent
{
    public override string EventType => EventTypes.Orders.Submitted;

    public required Guid OrderId { get; init; }

    public required string ClientOrderId { get; init; }

    /// <summary>Exchange-assigned order id. Absent in paper mode.</summary>
    public string? ExchangeOrderId { get; init; }

    public required string Symbol { get; init; }

    /// <summary>Round-trip latency to the exchange, for the execution latency metric.</summary>
    public required int SubmissionLatencyMs { get; init; }

    public required string TradingMode { get; init; }
}

/// <summary>The order filled in part.</summary>
public sealed record OrderPartiallyFilled : IntegrationEvent
{
    public override string EventType => EventTypes.Orders.PartiallyFilled;

    public required Guid OrderId { get; init; }

    public required string Symbol { get; init; }

    /// <summary>Cumulative filled quantity, not the increment, so the event is idempotent.</summary>
    public required decimal CumulativeFilledQuantity { get; init; }

    public required decimal RemainingQuantity { get; init; }

    /// <summary>Volume-weighted average fill price so far.</summary>
    public required decimal AverageFillPrice { get; init; }

    public required decimal CumulativeFeePaid { get; init; }

    public required string FeeAsset { get; init; }
}

/// <summary>The order filled completely.</summary>
public sealed record OrderFilled : IntegrationEvent
{
    public override string EventType => EventTypes.Orders.Filled;

    public required Guid OrderId { get; init; }

    public required Guid TradingIntentId { get; init; }

    public required string Symbol { get; init; }

    public required string Side { get; init; }

    public required decimal FilledQuantity { get; init; }

    public required decimal AverageFillPrice { get; init; }

    /// <summary>Total order value actually transacted, in quote asset.</summary>
    public required decimal FilledNotional { get; init; }

    /// <summary>Exchange fee charged. Always recorded: unaccounted fees make P&amp;L wrong.</summary>
    public required decimal FeePaid { get; init; }

    public required string FeeAsset { get; init; }

    public string? ExchangeOrderId { get; init; }

    public required DateTimeOffset FilledAt { get; init; }

    public required string TradingMode { get; init; }
}

/// <summary>The order was cancelled, by the platform or on the exchange.</summary>
public sealed record OrderCancelled : IntegrationEvent
{
    public override string EventType => EventTypes.Orders.Cancelled;

    public required Guid OrderId { get; init; }

    public required string Symbol { get; init; }

    /// <summary>Quantity that had already filled before cancellation.</summary>
    public required decimal FilledQuantityBeforeCancel { get; init; }

    public required string Reason { get; init; }

    /// <summary>What cancelled it: <c>OPERATOR</c>, <c>KILL_SWITCH</c> or <c>EXCHANGE</c>.</summary>
    public required string CancelledBy { get; init; }
}

/// <summary>The exchange rejected the order outright.</summary>
public sealed record OrderRejected : IntegrationEvent
{
    public override string EventType => EventTypes.Orders.Rejected;

    public required Guid OrderId { get; init; }

    public required string Symbol { get; init; }

    /// <summary>Exchange error code, retained verbatim for diagnosis.</summary>
    public required string ExchangeErrorCode { get; init; }

    public required string ExchangeErrorMessage { get; init; }

    /// <summary>
    /// Whether retrying is safe. False for anything that may already have
    /// reached the book; such orders go to reconciliation rather than a retry.
    /// </summary>
    public required bool IsRetryable { get; init; }
}

/// <summary>
/// The true outcome of an order could not be established.
/// </summary>
/// <remarks>
/// The most dangerous order state there is: a submission timed out, so the
/// platform does not know whether an order rests on the book. Automated trading
/// on the symbol stops and reconciliation takes over. Retrying blind here is how
/// a system ends up with twice the intended position.
/// </remarks>
public sealed record OrderIndeterminate : IntegrationEvent
{
    public override string EventType => EventTypes.Orders.Indeterminate;

    public required Guid OrderId { get; init; }

    public required string ClientOrderId { get; init; }

    public required string Symbol { get; init; }

    public required string Detail { get; init; }

    /// <summary>What was attempted when the outcome became unknown.</summary>
    public required string LastKnownAction { get; init; }
}
