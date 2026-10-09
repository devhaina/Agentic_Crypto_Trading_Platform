using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Execution.Application.Abstractions;

namespace Agentiva.Execution.Application.Orders;

/// <summary>Fetches one order.</summary>
/// <param name="OrderId">Order identity.</param>
public sealed record GetOrderQuery(Guid OrderId) : IQuery<Result<OrderResponse>>;

/// <summary>Lists recent orders, newest first.</summary>
/// <param name="Limit">Maximum rows to return.</param>
public sealed record ListOrdersQuery(int Limit = 50) : IQuery<Result<IReadOnlyList<OrderResponse>>>;

/// <summary>
/// Whether an order on this symbol and side is currently open.
/// </summary>
/// <remarks>
/// Backs the Trading Service's real duplicate-order check: it calls this
/// before submitting an intent to the risk gate, and the risk gate's
/// <c>HasDuplicateOpenOrder</c> input is this query's answer rather than the
/// hard-coded <c>false</c> Phase 1 shipped. See
/// docs/architecture/known-limitations.md.
/// </remarks>
/// <param name="Symbol">Trading pair.</param>
/// <param name="Side">Trade direction.</param>
public sealed record HasOpenOrderQuery(string Symbol, string Side) : IQuery<Result<bool>>;

/// <summary>Handles <see cref="GetOrderQuery"/>.</summary>
public sealed class GetOrderQueryHandler(IOrderRepository orders)
    : IRequestHandler<GetOrderQuery, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> HandleAsync(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(OrderId.From(request.OrderId), cancellationToken);

        return order is null
            ? Result.Failure<OrderResponse>(Error.NotFound(
                "execution.order.not_found", $"No order exists with id {request.OrderId}."))
            : Result.Success(SubmitOrderCommandHandler.ToResponse(order));
    }
}

/// <summary>Handles <see cref="ListOrdersQuery"/>.</summary>
public sealed class ListOrdersQueryHandler(IOrderRepository orders)
    : IRequestHandler<ListOrdersQuery, Result<IReadOnlyList<OrderResponse>>>
{
    public async Task<Result<IReadOnlyList<OrderResponse>>> HandleAsync(
        ListOrdersQuery request, CancellationToken cancellationToken)
    {
        var recent = await orders.ListRecentAsync(request.Limit, cancellationToken);

        IReadOnlyList<OrderResponse> responses = recent
            .Select(SubmitOrderCommandHandler.ToResponse)
            .ToArray();

        return Result.Success(responses);
    }
}

/// <summary>Handles <see cref="HasOpenOrderQuery"/>.</summary>
public sealed class HasOpenOrderQueryHandler(IOrderRepository orders)
    : IRequestHandler<HasOpenOrderQuery, Result<bool>>
{
    public async Task<Result<bool>> HandleAsync(HasOpenOrderQuery request, CancellationToken cancellationToken)
    {
        if (!Symbol.TryCreate(request.Symbol, out var symbol))
        {
            return Result.Failure<bool>(Error.Validation(
                "execution.order.invalid_symbol", $"'{request.Symbol}' is not a valid exchange symbol."));
        }

        var side = string.Equals(request.Side, "BUY", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Buy
            : OrderSide.Sell;

        var hasOpenOrder = await orders.HasOpenOrderAsync(symbol, side, cancellationToken);
        return Result.Success(hasOpenOrder);
    }
}
