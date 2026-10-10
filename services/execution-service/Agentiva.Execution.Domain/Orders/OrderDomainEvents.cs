using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.Contracts.Events;

namespace Agentiva.Execution.Domain.Orders;

/// <summary>
/// Raised when an order is recorded locally, before exchange submission.
/// </summary>
/// <remarks>
/// Field names mirror <see cref="Contracts.Events.Orders.OrderCreated"/> exactly.
/// The outbox publishes this event's own serialised JSON under that event's
/// routing key, so the two shapes must stay in lock-step even though they are
/// different CLR types — see the equivalent remark on
/// <c>Trading.Domain.Intents.TradingIntentEvents</c>.
/// </remarks>
public sealed record OrderCreatedDomainEvent : DomainEvent
{
    public OrderCreatedDomainEvent(
        DateTimeOffset occurredAt,
        Guid orderId,
        Guid tradingIntentId,
        Guid riskCheckId,
        string symbol,
        string side,
        string orderType,
        decimal quantity,
        decimal? price,
        string clientOrderId,
        string tradingMode)
        : base(occurredAt)
    {
        OrderId = orderId;
        TradingIntentId = tradingIntentId;
        RiskCheckId = riskCheckId;
        Symbol = symbol;
        Side = side;
        OrderType = orderType;
        Quantity = quantity;
        Price = price;
        ClientOrderId = clientOrderId;
        TradingMode = tradingMode;
    }

    public override string EventType => EventTypes.Orders.Created;

    public Guid OrderId { get; }

    public Guid TradingIntentId { get; }

    public Guid RiskCheckId { get; }

    public string Symbol { get; }

    public string Side { get; }

    public string OrderType { get; }

    public decimal Quantity { get; }

    public decimal? Price { get; }

    public string ClientOrderId { get; }

    public string TradingMode { get; }
}

/// <summary>Raised when the order has been handed to the exchange and acknowledged.</summary>
public sealed record OrderSubmittedDomainEvent : DomainEvent
{
    public OrderSubmittedDomainEvent(
        DateTimeOffset occurredAt,
        Guid orderId,
        string clientOrderId,
        string? exchangeOrderId,
        string symbol,
        int submissionLatencyMs,
        string tradingMode)
        : base(occurredAt)
    {
        OrderId = orderId;
        ClientOrderId = clientOrderId;
        ExchangeOrderId = exchangeOrderId;
        Symbol = symbol;
        SubmissionLatencyMs = submissionLatencyMs;
        TradingMode = tradingMode;
    }

    public override string EventType => EventTypes.Orders.Submitted;

    public Guid OrderId { get; }

    public string ClientOrderId { get; }

    public string? ExchangeOrderId { get; }

    public string Symbol { get; }

    public int SubmissionLatencyMs { get; }

    public string TradingMode { get; }
}

/// <summary>Raised when the order fills in part.</summary>
public sealed record OrderPartiallyFilledDomainEvent : DomainEvent
{
    public OrderPartiallyFilledDomainEvent(
        DateTimeOffset occurredAt,
        Guid orderId,
        Guid tradingAccountId,
        string symbol,
        decimal cumulativeFilledQuantity,
        decimal remainingQuantity,
        decimal averageFillPrice,
        decimal cumulativeFeePaid,
        string feeAsset)
        : base(occurredAt)
    {
        OrderId = orderId;
        TradingAccountId = tradingAccountId;
        Symbol = symbol;
        CumulativeFilledQuantity = cumulativeFilledQuantity;
        RemainingQuantity = remainingQuantity;
        AverageFillPrice = averageFillPrice;
        CumulativeFeePaid = cumulativeFeePaid;
        FeeAsset = feeAsset;
    }

    public override string EventType => EventTypes.Orders.PartiallyFilled;

    public Guid OrderId { get; }

    public Guid TradingAccountId { get; }

    public string Symbol { get; }

    public decimal CumulativeFilledQuantity { get; }

    public decimal RemainingQuantity { get; }

    public decimal AverageFillPrice { get; }

    public decimal CumulativeFeePaid { get; }

    public string FeeAsset { get; }
}

/// <summary>Raised when the order fills completely.</summary>
public sealed record OrderFilledDomainEvent : DomainEvent
{
    public OrderFilledDomainEvent(
        DateTimeOffset occurredAt,
        Guid orderId,
        Guid tradingIntentId,
        Guid tradingAccountId,
        string symbol,
        string side,
        decimal filledQuantity,
        decimal averageFillPrice,
        decimal filledNotional,
        decimal feePaid,
        string feeAsset,
        string? exchangeOrderId,
        DateTimeOffset filledAt,
        string tradingMode)
        : base(occurredAt)
    {
        OrderId = orderId;
        TradingIntentId = tradingIntentId;
        TradingAccountId = tradingAccountId;
        Symbol = symbol;
        Side = side;
        FilledQuantity = filledQuantity;
        AverageFillPrice = averageFillPrice;
        FilledNotional = filledNotional;
        FeePaid = feePaid;
        FeeAsset = feeAsset;
        ExchangeOrderId = exchangeOrderId;
        FilledAt = filledAt;
        TradingMode = tradingMode;
    }

    public override string EventType => EventTypes.Orders.Filled;

    public Guid OrderId { get; }

    public Guid TradingIntentId { get; }

    public Guid TradingAccountId { get; }

    public string Symbol { get; }

    public string Side { get; }

    public decimal FilledQuantity { get; }

    public decimal AverageFillPrice { get; }

    public decimal FilledNotional { get; }

    public decimal FeePaid { get; }

    public string FeeAsset { get; }

    public string? ExchangeOrderId { get; }

    public DateTimeOffset FilledAt { get; }

    public string TradingMode { get; }
}

/// <summary>Raised when the order is cancelled, by the platform or on the exchange.</summary>
public sealed record OrderCancelledDomainEvent : DomainEvent
{
    public OrderCancelledDomainEvent(
        DateTimeOffset occurredAt,
        Guid orderId,
        string symbol,
        decimal filledQuantityBeforeCancel,
        string reason,
        string cancelledBy)
        : base(occurredAt)
    {
        OrderId = orderId;
        Symbol = symbol;
        FilledQuantityBeforeCancel = filledQuantityBeforeCancel;
        Reason = reason;
        CancelledBy = cancelledBy;
    }

    public override string EventType => EventTypes.Orders.Cancelled;

    public Guid OrderId { get; }

    public string Symbol { get; }

    public decimal FilledQuantityBeforeCancel { get; }

    public string Reason { get; }

    public string CancelledBy { get; }
}

/// <summary>Raised when the exchange rejects the order outright.</summary>
public sealed record OrderRejectedDomainEvent : DomainEvent
{
    public OrderRejectedDomainEvent(
        DateTimeOffset occurredAt,
        Guid orderId,
        string symbol,
        string exchangeErrorCode,
        string exchangeErrorMessage,
        bool isRetryable)
        : base(occurredAt)
    {
        OrderId = orderId;
        Symbol = symbol;
        ExchangeErrorCode = exchangeErrorCode;
        ExchangeErrorMessage = exchangeErrorMessage;
        IsRetryable = isRetryable;
    }

    public override string EventType => EventTypes.Orders.Rejected;

    public Guid OrderId { get; }

    public string Symbol { get; }

    public string ExchangeErrorCode { get; }

    public string ExchangeErrorMessage { get; }

    public bool IsRetryable { get; }
}

/// <summary>Raised when the true outcome of an order could not be established.</summary>
public sealed record OrderIndeterminateDomainEvent : DomainEvent
{
    public OrderIndeterminateDomainEvent(
        DateTimeOffset occurredAt,
        Guid orderId,
        string clientOrderId,
        string symbol,
        string detail,
        string lastKnownAction)
        : base(occurredAt)
    {
        OrderId = orderId;
        ClientOrderId = clientOrderId;
        Symbol = symbol;
        Detail = detail;
        LastKnownAction = lastKnownAction;
    }

    public override string EventType => EventTypes.Orders.Indeterminate;

    public Guid OrderId { get; }

    public string ClientOrderId { get; }

    public string Symbol { get; }

    public string Detail { get; }

    public string LastKnownAction { get; }
}
