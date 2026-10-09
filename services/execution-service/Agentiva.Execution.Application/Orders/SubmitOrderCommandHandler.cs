using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Application.Configuration;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Execution.Application.Abstractions;
using Agentiva.Execution.Domain.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.Execution.Application.Orders;

/// <summary>
/// Drives the execution workflow: record the order, resolve the adapter for
/// the platform's actual trading mode, place it, record the outcome.
/// </summary>
/// <remarks>
/// The ordering mirrors <c>Trading.Application.Intents.CreateTradingIntentCommandHandler</c>
/// deliberately: the order is persisted <em>before</em> the exchange is
/// contacted, so a crash mid-workflow leaves a recoverable <see cref="OrderStatus.Created"/>
/// row rather than losing the record of an order that may have reached the book.
/// </remarks>
public sealed class SubmitOrderCommandHandler(
    IOrderRepository orders,
    IExchangeExecutionResolver executionResolver,
    IUnitOfWork unitOfWork,
    IOptions<TradingOptions> tradingOptions,
    IClock clock,
    ILogger<SubmitOrderCommandHandler> logger)
    : IRequestHandler<SubmitOrderCommand, Result<OrderResponse>>
{
    private readonly TradingOptions _trading = tradingOptions.Value;

    public async Task<Result<OrderResponse>> HandleAsync(
        SubmitOrderCommand command,
        CancellationToken cancellationToken)
    {
        var symbol = Symbol.Create(command.Symbol);

        var side = string.Equals(command.Side, "BUY", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Buy
            : OrderSide.Sell;

        var orderType = string.Equals(command.OrderType, "LIMIT", StringComparison.OrdinalIgnoreCase)
            ? OrderType.Limit
            : OrderType.Market;

        var quantity = Quantity.Create(command.Quantity);
        var limitPrice = command.LimitPrice is { } lp ? Price.Create(lp) : (Price?)null;

        // Re-derived from this service's own configuration, never trusted
        // from the caller — see the remarks on SubmitOrderCommand.
        var effectiveMode = _trading.EffectiveMode;

        if (effectiveMode == TradingMode.Backtest)
        {
            // Backtest makes no exchange contact whatsoever — not even a
            // simulated fill through this service. A backtest run belongs to
            // the Backtesting Service (Phase 8); nothing in the live pipeline
            // should route an order here while the platform is in this mode.
            logger.LogError(
                "Refusing to submit an order for {Symbol}: trading mode is Backtest, which makes no "
                + "exchange contact of any kind.",
                symbol);

            return Result.Failure<OrderResponse>(Error.Validation(
                "execution.order.backtest_mode",
                "Backtest mode places no exchange orders. This request should never have reached "
                + "the Execution Service."));
        }

        var clientOrderId = ClientOrderIdGenerator.Generate(symbol.Value, command.IdempotencyKey, clock.UtcNow);

        var order = Order.Create(
            tradingIntentId: TradingIntentId.From(command.TradingIntentId),
            riskCheckId: RiskCheckId.From(command.RiskCheckId),
            tradingAccountId: TradingAccountId.From(command.TradingAccountId),
            symbol: symbol,
            side: side,
            orderType: orderType,
            quantity: quantity,
            limitPrice: limitPrice,
            clientOrderId: clientOrderId,
            tradingMode: effectiveMode,
            now: clock.UtcNow);

        orders.Add(order);

        // Committed before the exchange call. A crash after this point leaves
        // a visible order in Created, which an operator or a future
        // reconciliation sweep can resolve — far better than losing the
        // record of an order that may have reached the book.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Recorded order {OrderId} ({ClientOrderId}) from intent {TradingIntentId} for {Symbol} {Side} "
            + "{Quantity} in {TradingMode} mode; placing it.",
            order.Id,
            clientOrderId,
            command.TradingIntentId,
            symbol,
            side,
            quantity,
            effectiveMode);

        var adapter = executionResolver.Resolve(effectiveMode);

        var exchangeRequest = new ExchangeOrderRequest(
            Symbol: symbol.Value,
            Side: side,
            OrderType: orderType,
            Quantity: quantity.Value,
            LimitPrice: limitPrice?.Value,
            ReferencePrice: command.ReferencePrice,
            ClientOrderId: clientOrderId);

        var placement = await adapter.PlaceOrderAsync(exchangeRequest, cancellationToken);

        if (placement.IsFailure)
        {
            // An unavailable exchange (timeout, unreachable) leaves the true
            // outcome unknown — the order may already rest on the book — and
            // must never be treated as a plain rejection. Anything else is an
            // outright rejection, which only an un-submitted order can record.
            if (placement.Error.Type == ErrorType.Unavailable)
            {
                order.MarkIndeterminate(placement.Error.Message, "PlaceOrder", clock.UtcNow);

                logger.LogError(
                    "Order {OrderId} for {Symbol} has an indeterminate outcome: {Reason}. "
                    + "Automated trading on this symbol must stop for reconciliation.",
                    order.Id,
                    symbol,
                    placement.Error);
            }
            else
            {
                order.Reject(placement.Error.Code, placement.Error.Message, isRetryable: false, clock.UtcNow);

                logger.LogWarning(
                    "Order {OrderId} for {Symbol} was rejected: {Reason}",
                    order.Id,
                    symbol,
                    placement.Error);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToResponse(order));
        }

        var ack = placement.Value;
        order.Submit(ack.ExchangeOrderId, ack.SubmissionLatencyMs, clock.UtcNow);

        switch (ack.Status)
        {
            case "FILLED":
                order.RecordFill(
                    Quantity.Create(ack.CumulativeFilledQuantity),
                    Price.Create(ack.AverageFillPrice),
                    ack.CumulativeFeePaid,
                    ack.FeeAsset,
                    filledAt: clock.UtcNow,
                    now: clock.UtcNow);
                break;

            case "PARTIALLY_FILLED":
                order.RecordPartialFill(
                    Quantity.Create(ack.CumulativeFilledQuantity),
                    Price.Create(ack.AverageFillPrice),
                    ack.CumulativeFeePaid,
                    ack.FeeAsset,
                    clock.UtcNow);
                break;

            default:
                // NEW (or anything else unrecognised): resting on the book,
                // unfilled so far. The order stays Submitted until a fill
                // event arrives — there is no such consumer yet in Phase 5;
                // see docs/architecture/known-limitations.md.
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Order {OrderId} ({ClientOrderId}) for {Symbol} {Side} {Quantity} reached {Status} in "
            + "{TradingMode} mode.",
            order.Id,
            clientOrderId,
            symbol,
            side,
            quantity,
            order.Status,
            effectiveMode);

        return Result.Success(ToResponse(order));
    }

    internal static OrderResponse ToResponse(Order order)
        => new(
            order.Id.Value,
            order.TradingIntentId.Value,
            order.Symbol.Value,
            order.Side.ToString().ToUpperInvariant(),
            order.OrderType.ToString().ToUpperInvariant(),
            order.Quantity.Value,
            order.LimitPrice?.Value,
            order.ClientOrderId,
            order.ExchangeOrderId,
            order.Status.ToString(),
            order.FilledQuantity.Value,
            order.AverageFillPrice?.Value,
            order.FeePaid,
            string.IsNullOrEmpty(order.FeeAsset) ? null : order.FeeAsset,
            order.TradingMode.ToString().ToUpperInvariant(),
            order.RejectionCode,
            order.RejectionDetail,
            order.CreatedAt,
            order.UpdatedAt);
}
