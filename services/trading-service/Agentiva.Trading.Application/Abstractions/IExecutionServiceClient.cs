using Agentiva.BuildingBlocks.Common.Results;

namespace Agentiva.Trading.Application.Abstractions;

/// <summary>
/// Synchronous client for the Execution Service.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IRiskServiceClient"/> deliberately: every failure mode
/// maps to a <see cref="Result"/> failure rather than an exception, and
/// placement is not retried on an ambiguous failure — a timeout may mean the
/// order already reached the exchange, and a blind retry risks a second one.
/// </remarks>
public interface IExecutionServiceClient
{
    /// <summary>
    /// Whether an order on this symbol and side is currently open — the real
    /// input behind <see cref="RiskEvaluationRequestDto.HasDuplicateOpenOrder"/>.
    /// </summary>
    /// <returns>
    /// The answer, or a failure when the Execution Service could not be
    /// reached. The caller treats an unreachable check as the conservative
    /// answer (open), never as "no duplicate" — see
    /// <c>CreateTradingIntentCommandHandler</c>.
    /// </returns>
    Task<Result<bool>> HasOpenOrderAsync(string symbol, string side, CancellationToken cancellationToken);

    /// <summary>Submits a risk-approved intent for execution.</summary>
    Task<Result<ExecutionOrderResult>> SubmitOrderAsync(
        ExecutionOrderRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

/// <summary>Request sent to the Execution Service to place an order.</summary>
/// <param name="TradingIntentId">The intent this order executes.</param>
/// <param name="RiskCheckId">The risk evaluation that approved it.</param>
/// <param name="TradingAccountId">The account the order is placed for.</param>
/// <param name="Symbol">Trading pair.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="OrderType"><c>MARKET</c> or <c>LIMIT</c>.</param>
/// <param name="Quantity">Approved base-asset quantity.</param>
/// <param name="LimitPrice">Limit price, when applicable.</param>
/// <param name="ReferencePrice">
/// The risk gate's effective entry price, used to mark a simulated fill in
/// Backtest/Paper mode.
/// </param>
public sealed record ExecutionOrderRequestDto(
    Guid TradingIntentId,
    Guid RiskCheckId,
    Guid TradingAccountId,
    string Symbol,
    string Side,
    string OrderType,
    decimal Quantity,
    decimal? LimitPrice,
    decimal ReferencePrice);

/// <summary>The order's state as the trading workflow sees it.</summary>
/// <param name="OrderId">Order identity.</param>
/// <param name="Status">Lifecycle state, e.g. <c>Filled</c>, <c>Submitted</c>, <c>Rejected</c>.</param>
/// <param name="FilledQuantity">Quantity filled so far.</param>
public sealed record ExecutionOrderResult(Guid OrderId, string Status, decimal FilledQuantity)
{
    public bool IsFilled => string.Equals(Status, "Filled", StringComparison.OrdinalIgnoreCase);

    public bool IsResting => Status is "Submitted" or "PartiallyFilled";
}
