using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Execution.Application.Abstractions;

/// <summary>
/// Places an order against an exchange, real or simulated.
/// </summary>
/// <remarks>
/// Every failure mode maps to a <see cref="Result"/> failure rather than an
/// exception, mirroring <c>Trading.Application.Abstractions.IRiskServiceClient</c>:
/// the one outcome this interface will never produce is a fabricated fill.
/// </remarks>
public interface IExchangeExecution
{
    /// <summary>
    /// Whether this implementation contacts a real exchange with real funds.
    /// Exactly one registered implementation may report <c>true</c>; see
    /// <see cref="IExchangeExecutionResolver"/>.
    /// </summary>
    bool SupportsLiveTrading { get; }

    /// <summary>Submits an order.</summary>
    Task<Result<ExchangePlacementResult>> PlaceOrderAsync(
        ExchangeOrderRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Selects the exchange adapter for a given trading mode.</summary>
/// <remarks>
/// The actual enforcement point for the trading-mode boundary: configuration
/// alone is not the control, so this is re-derived from this service's own
/// <c>TradingOptions</c> rather than trusted from the caller — see the remarks
/// on <c>TradingOptions.EffectiveMode</c>.
/// </remarks>
public interface IExchangeExecutionResolver
{
    /// <exception cref="InvalidOperationException">
    /// No adapter is registered for the requested mode.
    /// </exception>
    IExchangeExecution Resolve(TradingMode mode);
}

/// <summary>An order to place, independent of which adapter executes it.</summary>
/// <param name="Symbol">Trading pair, e.g. <c>BTCUSDT</c>.</param>
/// <param name="Side">Trade direction.</param>
/// <param name="OrderType">Market or limit.</param>
/// <param name="Quantity">Base-asset quantity.</param>
/// <param name="LimitPrice">Limit price. Null for a market order.</param>
/// <param name="ReferencePrice">
/// Price a simulated fill is marked against. Ignored by an adapter that
/// contacts a real exchange, which prices the fill itself.
/// </param>
/// <param name="ClientOrderId">Deterministic id sent as <c>newClientOrderId</c>.</param>
public sealed record ExchangeOrderRequest(
    string Symbol,
    OrderSide Side,
    OrderType OrderType,
    decimal Quantity,
    decimal? LimitPrice,
    decimal ReferencePrice,
    string ClientOrderId);

/// <summary>Outcome of a successful placement call.</summary>
/// <param name="ExchangeOrderId">Exchange-assigned order id. Null in a simulated mode.</param>
/// <param name="Status">
/// Exchange order status at the moment of the synchronous response:
/// <c>NEW</c>, <c>PARTIALLY_FILLED</c> or <c>FILLED</c>.
/// </param>
/// <param name="CumulativeFilledQuantity">Quantity filled so far. Zero for <c>NEW</c>.</param>
/// <param name="AverageFillPrice">Volume-weighted average fill price so far.</param>
/// <param name="CumulativeFeePaid">Fee charged so far.</param>
/// <param name="FeeAsset">Asset the fee was charged in.</param>
/// <param name="SubmissionLatencyMs">Round-trip latency to the exchange.</param>
public sealed record ExchangePlacementResult(
    string? ExchangeOrderId,
    string Status,
    decimal CumulativeFilledQuantity,
    decimal AverageFillPrice,
    decimal CumulativeFeePaid,
    string FeeAsset,
    int SubmissionLatencyMs);
