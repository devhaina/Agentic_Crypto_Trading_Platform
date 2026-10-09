using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.ServiceDefaults.Security;
using Agentiva.Execution.Application.Orders;
using Microsoft.AspNetCore.Mvc;

namespace Agentiva.Execution.Api.Endpoints;

/// <summary>The Execution Service's HTTP surface.</summary>
public static class ExecutionEndpoints
{
    /// <summary>Maps the execution endpoints under <c>/api/v1/orders</c>.</summary>
    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/orders")
            .WithTags("Orders");

        // ---------------------------------------------------------------------
        // POST /api/v1/orders — place an order for a risk-approved intent.
        // ---------------------------------------------------------------------
        group.MapPost("/", async (
                [FromBody] SubmitOrderRequest body,
                [FromHeader(Name = CorrelationHeaders.IdempotencyKey)] string? idempotencyKey,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    // Required, not optional. A submission can be replayed by
                    // an HTTP retry, and a second placement for the same
                    // intent would be a second order against real or
                    // simulated funds.
                    return Results.Problem(
                        title: "Idempotency key required",
                        detail: $"The {CorrelationHeaders.IdempotencyKey} header is required to submit an "
                                + "order so that a retried request cannot place a second one.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var command = new SubmitOrderCommand(
                    IdempotencyKey: idempotencyKey,
                    TradingIntentId: body.TradingIntentId,
                    RiskCheckId: body.RiskCheckId,
                    TradingAccountId: body.TradingAccountId,
                    Symbol: body.Symbol,
                    Side: body.Side,
                    OrderType: body.OrderType,
                    Quantity: body.Quantity,
                    LimitPrice: body.LimitPrice,
                    ReferencePrice: body.ReferencePrice);

                var result = await sender.SendAsync(command, cancellationToken);

                // A rejection or an indeterminate outcome is still a resolved
                // call: the order was recorded and its state reflects what
                // happened. Only an inability to process the request at all
                // is an error status.
                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : ToProblem(result.Error);
            })
            .WithName("SubmitOrder")
            .WithSummary("Places an order for a risk-approved trading intent.")
            .WithDescription(
                "Re-derives the effective trading mode from this service's own configuration rather "
                + "than trusting the caller: Backtest makes no exchange contact of any kind, Paper "
                + "simulates an immediate fill at the supplied reference price, and only Live places a "
                + "real order through BinanceExecutionAdapter.\n\n"
                + "Requires an Idempotency-Key header.")
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AgentivaPolicies.CanTrade);

        // ---------------------------------------------------------------------
        // GET /api/v1/orders/open — the real duplicate-order check.
        // ---------------------------------------------------------------------
        group.MapGet("/open", async (
                [FromQuery] string symbol,
                [FromQuery] string side,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new HasOpenOrderQuery(symbol, side), cancellationToken);
                return result.IsSuccess ? Results.Ok(new HasOpenOrderResponse(result.Value)) : ToProblem(result.Error);
            })
            .WithName("HasOpenOrder")
            .WithSummary("Reports whether an order on this symbol and side is currently open.")
            .WithDescription(
                "What the Trading Service's HasDuplicateOpenOrder risk input is actually built from, "
                + "replacing the hard-coded false Phase 1 shipped.")
            .Produces<HasOpenOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization(AgentivaPolicies.CanView);

        // ---------------------------------------------------------------------
        // Reads
        // ---------------------------------------------------------------------
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetOrderQuery(id), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetOrder")
            .WithSummary("Fetches one order.")
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AgentivaPolicies.CanView);

        group.MapGet("/", async (
                [FromQuery] int? limit, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new ListOrdersQuery(limit ?? 50), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("ListOrders")
            .WithSummary("Lists recent orders, newest first.")
            .Produces<IReadOnlyList<OrderResponse>>()
            .RequireAuthorization(AgentivaPolicies.CanView);

        return endpoints;
    }

    /// <summary>Maps a domain error onto an RFC 9457 problem document.</summary>
    private static IResult ToProblem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,

            // 503 rather than 500: an unavailable exchange is a condition the
            // caller may retry, and it must never be mistaken for a rejection.
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}

/// <summary>Request body to submit an order.</summary>
/// <param name="TradingIntentId">The intent this order executes.</param>
/// <param name="RiskCheckId">The risk evaluation that approved it.</param>
/// <param name="TradingAccountId">The account the order is placed for.</param>
/// <param name="Symbol">Trading pair, e.g. <c>BTCUSDT</c>.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="OrderType"><c>MARKET</c> or <c>LIMIT</c>.</param>
/// <param name="Quantity">Approved base-asset quantity.</param>
/// <param name="LimitPrice">Limit price. Required when <paramref name="OrderType"/> is <c>LIMIT</c>.</param>
/// <param name="ReferencePrice">Price a simulated fill is marked against in Backtest/Paper mode.</param>
public sealed record SubmitOrderRequest(
    Guid TradingIntentId,
    Guid RiskCheckId,
    Guid TradingAccountId,
    string Symbol,
    string Side,
    string OrderType,
    decimal Quantity,
    decimal? LimitPrice,
    decimal ReferencePrice);

/// <summary>Response body for the duplicate-order check.</summary>
/// <param name="HasOpenOrder">Whether an order on the requested symbol and side is currently open.</param>
public sealed record HasOpenOrderResponse(bool HasOpenOrder);
