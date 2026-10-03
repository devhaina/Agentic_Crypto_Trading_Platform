using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.ServiceDefaults.Security;
using Agentiva.Risk.Application.Evaluations;
using Agentiva.Risk.Application.Policies;
using Microsoft.AspNetCore.Mvc;

namespace Agentiva.Risk.Api.Endpoints;

/// <summary>The Risk Service's HTTP surface.</summary>
public static class RiskEndpoints
{
    /// <summary>Maps the risk endpoints under <c>/api/v1/risk</c>.</summary>
    public static IEndpointRouteBuilder MapRiskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/risk")
            .WithTags("Risk");

        // ---------------------------------------------------------------------
        // POST /api/v1/risk/evaluations — the deterministic gate.
        // ---------------------------------------------------------------------
        group.MapPost("/evaluations", async (
                [FromBody] EvaluateIntentRequest body,
                [FromHeader(Name = CorrelationHeaders.IdempotencyKey)] string? idempotencyKey,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    // Required, not optional. A risk evaluation can be replayed
                    // by an HTTP retry, and a second approval for the same
                    // intent could be executed as a second order.
                    return Results.Problem(
                        title: "Idempotency key required",
                        detail: $"The {CorrelationHeaders.IdempotencyKey} header is required on a risk "
                                + "evaluation so that a retried request cannot produce a second approval.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var command = new EvaluateIntentCommand(
                    IdempotencyKey: idempotencyKey,
                    TradingIntentId: body.TradingIntentId,
                    Symbol: body.Symbol,
                    Side: body.Side,
                    EntryPrice: body.EntryPrice,
                    StopLoss: body.StopLoss,
                    TakeProfit: body.TakeProfit,
                    Confidence: body.Confidence,
                    Portfolio: body.Portfolio,
                    SymbolVolatilityPercent: body.SymbolVolatilityPercent,
                    MarketDataAgeSeconds: body.MarketDataAgeSeconds,
                    IsExchangeAvailable: body.IsExchangeAvailable,
                    HasDuplicateOpenOrder: body.HasDuplicateOpenOrder,
                    RiskPolicyId: body.RiskPolicyId);

                var result = await sender.SendAsync(command, cancellationToken);

                // A rejection is a successful evaluation, so it returns 200 with
                // a Rejected decision — not a 4xx. The request was well formed
                // and the gate did its job; only an inability to evaluate at all
                // is an error status.
                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : ToProblem(result.Error);
            })
            .WithName("EvaluateTradingIntent")
            .WithSummary("Submits a trading intent to the deterministic risk gate.")
            .WithDescription(
                "Runs every configured risk check and, if all pass, derives the position size "
                + "deterministically from the risk budget. The approved quantity may be lower than "
                + "requested but is never higher.\n\n"
                + "A rejection returns HTTP 200 with decision=Rejected and the full check list: the "
                + "evaluation itself succeeded. HTTP 4xx/5xx mean the gate could not evaluate.\n\n"
                + "Requires an Idempotency-Key header.\n\n"
                + "**Phase 1:** the portfolio snapshot is supplied by the caller. From Phase 6 this "
                + "service fetches it from the Portfolio Service itself.")
            .Produces<RiskEvaluationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AgentivaPolicies.CanTrade);

        // ---------------------------------------------------------------------
        // Policies
        // ---------------------------------------------------------------------
        group.MapGet("/policies", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetRiskPoliciesQuery(), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetRiskPolicies")
            .WithSummary("Lists the configured risk policies.")
            .Produces<IReadOnlyList<RiskPolicyDto>>()
            .RequireAuthorization(AgentivaPolicies.CanView);

        group.MapGet("/policies/default", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetDefaultRiskPolicyQuery(), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetDefaultRiskPolicy")
            .WithSummary("Returns the active default risk policy.")
            .Produces<RiskPolicyDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
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

            // 503 rather than 500: an unavailable risk policy or dependency is
            // a condition the caller may retry, and it must never be mistaken
            // for an approval.
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

/// <summary>Request body for a risk evaluation.</summary>
/// <param name="TradingIntentId">The intent being evaluated.</param>
/// <param name="Symbol">Trading pair, e.g. <c>BTCUSDT</c>.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="EntryPrice">Proposed entry price. Sent as a JSON string to preserve precision.</param>
/// <param name="StopLoss">Proposed protective stop.</param>
/// <param name="TakeProfit">Proposed target.</param>
/// <param name="Confidence">Signal confidence as a fraction between 0 and 1.</param>
/// <param name="Portfolio">Current portfolio state.</param>
/// <param name="SymbolVolatilityPercent">Observed annualised volatility, in percent.</param>
/// <param name="MarketDataAgeSeconds">Age of the latest market data for the symbol.</param>
/// <param name="IsExchangeAvailable">Whether the exchange is reachable.</param>
/// <param name="HasDuplicateOpenOrder">Whether an equivalent order is already open.</param>
/// <param name="RiskPolicyId">Policy to apply. Omit to use the active default.</param>
public sealed record EvaluateIntentRequest(
    Guid TradingIntentId,
    string Symbol,
    string Side,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal Confidence,
    PortfolioSnapshotDto Portfolio,
    decimal SymbolVolatilityPercent,
    double MarketDataAgeSeconds,
    bool IsExchangeAvailable = true,
    bool HasDuplicateOpenOrder = false,
    Guid? RiskPolicyId = null);
