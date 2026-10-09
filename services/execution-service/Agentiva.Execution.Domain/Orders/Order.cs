using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Execution.Domain.Orders;

/// <summary>
/// An order placed, or about to be placed, against an exchange.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate at the centre of the execution workflow. It is created
/// <em>before</em> the exchange is contacted, which is what makes recovery
/// possible: if this process dies between creation and submission, the order
/// exists in a known, re-resolvable state rather than being lost — see the
/// remarks on <see cref="Orders.OrderCreatedDomainEvent"/>.
/// </para>
/// <para>
/// State transitions are enforced here, mirroring
/// <c>Trading.Domain.Intents.TradingIntent</c>: an order cannot be marked
/// filled without first being submitted, and a rejection is recorded only
/// against an order that was never acknowledged — an exchange does not
/// "reject" an order it already accepted.
/// </para>
/// </remarks>
public sealed class Order : AggregateRoot<OrderId>
{
    private Order()
    {
        // EF Core materialisation.
    }

    private Order(OrderId id)
        : base(id)
    {
    }

    public TradingIntentId TradingIntentId { get; private set; }

    public RiskCheckId RiskCheckId { get; private set; }

    public TradingAccountId TradingAccountId { get; private set; }

    public Symbol Symbol { get; private set; }

    public OrderSide Side { get; private set; }

    public OrderType OrderType { get; private set; }

    public Quantity Quantity { get; private set; }

    /// <summary>Limit price. Null for a market order.</summary>
    public Price? LimitPrice { get; private set; }

    /// <summary>
    /// Deterministic id sent to the exchange as <c>newClientOrderId</c>. See
    /// <see cref="ClientOrderIdGenerator"/>.
    /// </summary>
    public string ClientOrderId { get; private set; } = string.Empty;

    /// <summary>Exchange-assigned order id, once submitted. Absent in simulated modes.</summary>
    public string? ExchangeOrderId { get; private set; }

    public TradingMode TradingMode { get; private set; }

    public OrderStatus Status { get; private set; } = OrderStatus.Created;

    public Quantity FilledQuantity { get; private set; } = Quantity.Zero;

    public Price? AverageFillPrice { get; private set; }

    public decimal FeePaid { get; private set; }

    public string FeeAsset { get; private set; } = string.Empty;

    /// <summary>Exchange error code, when rejected. Retained verbatim for diagnosis.</summary>
    public string? RejectionCode { get; private set; }

    public string? RejectionDetail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records a new order and raises its creation event.</summary>
    public static Order Create(
        TradingIntentId tradingIntentId,
        RiskCheckId riskCheckId,
        TradingAccountId tradingAccountId,
        Symbol symbol,
        OrderSide side,
        OrderType orderType,
        Quantity quantity,
        Price? limitPrice,
        string clientOrderId,
        TradingMode tradingMode,
        DateTimeOffset now)
    {
        if (quantity.IsZero)
        {
            throw new DomainException(
                "execution.order.zero_quantity", "An order must request a non-zero quantity.");
        }

        if (orderType == OrderType.Limit && limitPrice is null)
        {
            throw new DomainException(
                "execution.order.limit_without_price", "A limit order must carry a limit price.");
        }

        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            throw new DomainException(
                "execution.order.missing_client_order_id", "An order must carry a client order id.");
        }

        var order = new Order(OrderId.New())
        {
            TradingIntentId = tradingIntentId,
            RiskCheckId = riskCheckId,
            TradingAccountId = tradingAccountId,
            Symbol = symbol,
            Side = side,
            OrderType = orderType,
            Quantity = quantity,
            LimitPrice = limitPrice,
            ClientOrderId = clientOrderId,
            TradingMode = tradingMode,
            Status = OrderStatus.Created,
            CreatedAt = now,
            UpdatedAt = now
        };

        order.Raise(new OrderCreatedDomainEvent(
            now,
            order.Id.Value,
            tradingIntentId.Value,
            riskCheckId.Value,
            symbol.Value,
            side.ToString().ToUpperInvariant(),
            orderType.ToString().ToUpperInvariant(),
            quantity.Value,
            limitPrice?.Value,
            clientOrderId,
            tradingMode.ToString().ToUpperInvariant()));

        return order;
    }

    /// <summary>Marks the order as handed to the exchange and acknowledged.</summary>
    /// <exception cref="DomainException">The order has already been submitted or resolved.</exception>
    public void Submit(string? exchangeOrderId, int submissionLatencyMs, DateTimeOffset now)
    {
        RequireStatus(now, OrderStatus.Submitted, OrderStatus.Created);

        ExchangeOrderId = exchangeOrderId;
        Status = OrderStatus.Submitted;

        Raise(new OrderSubmittedDomainEvent(
            now, Id.Value, ClientOrderId, exchangeOrderId, Symbol.Value, submissionLatencyMs,
            TradingMode.ToString().ToUpperInvariant()));
    }

    /// <summary>Records a partial fill. <paramref name="cumulativeFilledQuantity"/> is cumulative, not an increment.</summary>
    public void RecordPartialFill(
        Quantity cumulativeFilledQuantity,
        Price averageFillPrice,
        decimal cumulativeFeePaid,
        string feeAsset,
        DateTimeOffset now)
    {
        RequireStatus(now, OrderStatus.PartiallyFilled, OrderStatus.Submitted, OrderStatus.PartiallyFilled);

        if (cumulativeFilledQuantity > Quantity)
        {
            throw new DomainException(
                "execution.order.fill_exceeds_quantity",
                $"A fill of {cumulativeFilledQuantity} exceeds the order's own quantity of {Quantity}.");
        }

        FilledQuantity = cumulativeFilledQuantity;
        AverageFillPrice = averageFillPrice;
        FeePaid = cumulativeFeePaid;
        FeeAsset = feeAsset;
        Status = OrderStatus.PartiallyFilled;

        var remaining = Quantity - cumulativeFilledQuantity;

        Raise(new OrderPartiallyFilledDomainEvent(
            now, Id.Value, Symbol.Value, cumulativeFilledQuantity.Value, remaining.Value,
            averageFillPrice.Value, cumulativeFeePaid, feeAsset));
    }

    /// <summary>Records a complete fill.</summary>
    public void RecordFill(
        Quantity filledQuantity,
        Price averageFillPrice,
        decimal feePaid,
        string feeAsset,
        DateTimeOffset filledAt,
        DateTimeOffset now)
    {
        RequireStatus(now, OrderStatus.Filled, OrderStatus.Submitted, OrderStatus.PartiallyFilled);

        if (filledQuantity > Quantity)
        {
            throw new DomainException(
                "execution.order.fill_exceeds_quantity",
                $"A fill of {filledQuantity} exceeds the order's own quantity of {Quantity}.");
        }

        FilledQuantity = filledQuantity;
        AverageFillPrice = averageFillPrice;
        FeePaid = feePaid;
        FeeAsset = feeAsset;
        Status = OrderStatus.Filled;

        var filledNotional = filledQuantity.Value * averageFillPrice.Value;

        Raise(new OrderFilledDomainEvent(
            now, Id.Value, TradingIntentId.Value, Symbol.Value, Side.ToString().ToUpperInvariant(),
            filledQuantity.Value, averageFillPrice.Value, filledNotional, feePaid, feeAsset,
            ExchangeOrderId, filledAt, TradingMode.ToString().ToUpperInvariant()));
    }

    /// <summary>Cancels an order that has not yet filled completely.</summary>
    public void Cancel(string reason, string cancelledBy, DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Created or OrderStatus.Submitted or OrderStatus.PartiallyFilled))
        {
            throw new DomainException(
                "execution.order.already_terminal",
                $"Order {Id} is already {Status} and cannot be cancelled.");
        }

        var filledBeforeCancel = FilledQuantity.Value;
        Status = OrderStatus.Cancelled;
        UpdatedAt = now;

        Raise(new OrderCancelledDomainEvent(now, Id.Value, Symbol.Value, filledBeforeCancel, reason, cancelledBy));
    }

    /// <summary>
    /// Records that the exchange rejected the order outright, before any
    /// acknowledgement.
    /// </summary>
    /// <exception cref="DomainException">The order has already been submitted.</exception>
    public void Reject(string exchangeErrorCode, string exchangeErrorMessage, bool isRetryable, DateTimeOffset now)
    {
        RequireStatus(now, OrderStatus.Rejected, OrderStatus.Created);

        RejectionCode = exchangeErrorCode;
        RejectionDetail = exchangeErrorMessage;
        Status = OrderStatus.Rejected;

        Raise(new OrderRejectedDomainEvent(now, Id.Value, Symbol.Value, exchangeErrorCode, exchangeErrorMessage, isRetryable));
    }

    /// <summary>
    /// Records that the true outcome of the order could not be established —
    /// typically a submission timeout. See the remarks on
    /// <see cref="Orders.OrderIndeterminateDomainEvent"/> for why this is
    /// never retried automatically.
    /// </summary>
    public void MarkIndeterminate(string detail, string lastKnownAction, DateTimeOffset now)
    {
        RequireStatus(now, OrderStatus.Unknown, OrderStatus.Created, OrderStatus.Submitted);

        Status = OrderStatus.Unknown;

        Raise(new OrderIndeterminateDomainEvent(now, Id.Value, ClientOrderId, Symbol.Value, detail, lastKnownAction));
    }

    private void RequireStatus(DateTimeOffset now, OrderStatus target, params OrderStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
        {
            throw new DomainException(
                "execution.order.invalid_transition",
                $"Order {Id} cannot move from {Status} to {target}. "
                + $"Allowed prior states: {string.Join(", ", allowedFrom)}.");
        }

        UpdatedAt = now;
    }
}
