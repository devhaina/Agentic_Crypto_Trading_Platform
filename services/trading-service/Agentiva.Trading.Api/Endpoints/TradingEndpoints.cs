using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.ServiceDefaults.Security;
using Agentiva.Trading.Application.Intents;
using Microsoft.AspNetCore.Mvc;

namespace Agentiva.Trading.Api.Endpoints;

/// <summary>The Trading Service's HTTP surface.</summary>
public static class TradingEndpoints
{
    /// <summary>Maps the trading endpoints under <c>/api/v1/trading</c>.</summary>
    public static IEndpointRouteBuilder MapTradingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/trading")
            .WithTags("Trading");

        // ---------------------------------------------------------------------
        // POST /api/v1/trading/intents — the single entry point to the workflow.
        // ---------------------------------------------------------------------
        group.MapPost("/intents", async (
                [FromBody] CreateTradingIntentRequest body,
                [FromHeader(Name = CorrelationHeaders.IdempotencyKey)] string? idempotencyKey,
                [FromHeader(Name = CorrelationHeaders.AgentRunId)] string? agentRunId,
                ISender sender,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    // Mandatory. An HTTP retry, a client-side timeout or a load
                    // balancer replay must not be able to create a second intent
                    // and therefore a second order.
                    return Results.Problem(
                        title: "Idempotency key required",
                        detail: $"The {CorrelationHeaders.IdempotencyKey} header is required when "
                                + "creating a trading intent, so that a retried request cannot create "
                                + "a duplicate order.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                // Taken from the authenticated principal, never from the body.
                // Letting a caller name its own actor would make the audit trail
                // worthless.
                var actor = httpContext.User.FindFirst("sub")?.Value
                            ?? httpContext.User.Identity?.Name
                            ?? "SYSTEM";

                _ = Guid.TryParse(agentRunId, out var parsedAgentRunId);

                var command = new CreateTradingIntentCommand(
                    IdempotencyKey: idempotencyKey,
                    TradingAccountId: body.TradingAccountId,
                    Symbol: body.Symbol,
                    Side: body.Side,
                    Quantity: body.Quantity,
                    EntryPrice: body.EntryPrice,
                    StopLoss: body.StopLoss,
                    TakeProfit: body.TakeProfit,
                    Confidence: body.Confidence,
                    Source: body.Source,
                    SignalId: body.SignalId,
                    StrategyId: body.StrategyId,
                    AgentRunId: parsedAgentRunId == Guid.Empty ? null : parsedAgentRunId,
                    CreatedBy: actor);

                var result = await sender.SendAsync(command, cancellationToken);

                if (result.IsFailure)
                {
                    return ToProblem(result.Error);
                }

                var intent = result.Value;

                // 201 regardless of the risk decision: the intent resource was
                // created either way, and its status field carries the outcome.
                // A rejection is a recorded decision, not a failed request.
                return Results.Created($"/api/v1/trading/intents/{intent.TradingIntentId}", intent);
            })
            .WithName("CreateTradingIntent")
            .WithSummary("Creates a trading intent and submits it to the deterministic risk gate.")
            .WithDescription(
                "The single entry point into the trading workflow, used identically by strategy "
                + "signals, AI proposals and manual requests — there is no second path that could "
                + "bypass the risk gate.\n\n"
                + "The requested quantity is an upper bound only: the Risk Service derives its own "
                + "position size and may approve less, never more.\n\n"
                + "Returns 201 whether the gate approves or rejects; inspect `status` and "
                + "`rejectionCodes`. Requires an Idempotency-Key header.")
            .Produces<TradingIntentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AgentivaPolicies.CanTrade);

        // ---------------------------------------------------------------------
        // Reads
        // ---------------------------------------------------------------------
        group.MapGet("/intents/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetTradingIntentQuery(id), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetTradingIntent")
            .WithSummary("Returns one trading intent and its workflow state.")
            .Produces<TradingIntentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AgentivaPolicies.CanView);

        group.MapGet("/intents", async (
                [FromQuery] int limit,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(
                    new ListTradingIntentsQuery(limit <= 0 ? 50 : limit), cancellationToken);

                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("ListTradingIntents")
            .WithSummary("Lists recent trading intents, newest first.")
            .Produces<IReadOnlyList<TradingIntentResponse>>()
            .RequireAuthorization(AgentivaPolicies.CanView);

        return endpoints;
    }

    private static IResult ToProblem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
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

/// <summary>Request body for creating a trading intent.</summary>
/// <param name="TradingAccountId">The account to trade on.</param>
/// <param name="Symbol">Trading pair, e.g. <c>BTCUSDT</c>.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="Quantity">
/// Requested base-asset quantity, as a JSON string. An upper bound: the risk
/// gate derives its own size and may approve less.
/// </param>
/// <param name="EntryPrice">Entry price. Omit to use the last traded price.</param>
/// <param name="StopLoss">Protective stop. Required by the default risk policy.</param>
/// <param name="TakeProfit">Target. Required by the default risk policy.</param>
/// <param name="Confidence">Confidence as a fraction between 0 and 1.</param>
/// <param name="Source"><c>STRATEGY</c>, <c>AGENT</c> or <c>MANUAL</c>.</param>
/// <param name="SignalId">Originating signal, when there was one.</param>
/// <param name="StrategyId">Originating strategy, when there was one.</param>
public sealed record CreateTradingIntentRequest(
    Guid TradingAccountId,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal Confidence,
    string Source,
    Guid? SignalId = null,
    Guid? StrategyId = null);
