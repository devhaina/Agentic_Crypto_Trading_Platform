using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;

namespace Agentiva.Execution.Application.Orders;

/// <summary>
/// Places an order for a risk-approved trading intent.
/// </summary>
/// <remarks>
/// The only entry point from the Trading Service into this one. The caller's
/// belief about the trading mode is not trusted: this service re-derives the
/// effective mode from its own configuration before deciding whether to
/// simulate the fill or contact a real exchange — see the remarks on
/// <c>TradingOptions.EffectiveMode</c>.
/// </remarks>
/// <param name="IdempotencyKey">Key guarding the whole submission.</param>
/// <param name="TradingIntentId">The intent this order executes.</param>
/// <param name="RiskCheckId">The risk evaluation that approved it.</param>
/// <param name="TradingAccountId">The account the order is placed for.</param>
/// <param name="Symbol">Trading pair, e.g. <c>BTCUSDT</c>.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="OrderType"><c>MARKET</c> or <c>LIMIT</c>.</param>
/// <param name="Quantity">Approved base-asset quantity.</param>
/// <param name="LimitPrice">Limit price. Required when <paramref name="OrderType"/> is <c>LIMIT</c>.</param>
/// <param name="ReferencePrice">
/// Price a simulated fill is marked against in Backtest/Paper mode — the risk
/// gate's effective entry price. Ignored once a real exchange prices the fill.
/// </param>
public sealed record SubmitOrderCommand(
    string IdempotencyKey,
    Guid TradingIntentId,
    Guid RiskCheckId,
    Guid TradingAccountId,
    string Symbol,
    string Side,
    string OrderType,
    decimal Quantity,
    decimal? LimitPrice,
    decimal ReferencePrice) : IFinancialCommand<Result<OrderResponse>>;

/// <summary>An order and its current state.</summary>
/// <param name="OrderId">Order identity.</param>
/// <param name="TradingIntentId">The intent this order executes.</param>
/// <param name="Symbol">Trading pair.</param>
/// <param name="Side">Trade direction.</param>
/// <param name="OrderType">Market or limit.</param>
/// <param name="Quantity">Requested quantity.</param>
/// <param name="LimitPrice">Limit price, when applicable.</param>
/// <param name="ClientOrderId">Deterministic id sent to the exchange.</param>
/// <param name="ExchangeOrderId">Exchange-assigned id, once submitted.</param>
/// <param name="Status">Current lifecycle state.</param>
/// <param name="FilledQuantity">Quantity filled so far.</param>
/// <param name="AverageFillPrice">Volume-weighted average fill price, once any fill exists.</param>
/// <param name="FeePaid">Fee charged so far.</param>
/// <param name="FeeAsset">Asset the fee was charged in.</param>
/// <param name="TradingMode">Effective trading mode this order executed under.</param>
/// <param name="RejectionCode">Exchange error code, when rejected.</param>
/// <param name="RejectionDetail">Exchange error detail, when rejected.</param>
/// <param name="CreatedAt">When the order was recorded.</param>
/// <param name="UpdatedAt">When the order last changed state.</param>
public sealed record OrderResponse(
    Guid OrderId,
    Guid TradingIntentId,
    string Symbol,
    string Side,
    string OrderType,
    decimal Quantity,
    decimal? LimitPrice,
    string ClientOrderId,
    string? ExchangeOrderId,
    string Status,
    decimal FilledQuantity,
    decimal? AverageFillPrice,
    decimal FeePaid,
    string? FeeAsset,
    string TradingMode,
    string? RejectionCode,
    string? RejectionDetail,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
