using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.ServiceDefaults.Security;
using Agentiva.Risk.Application.Evaluations;
using Agentiva.Risk.Application.Operations;
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

        // ---------------------------------------------------------------------
        // Policy CRUD — administrator only. The limits that constrain the
        // platform must not be editable through any path a trader or an AI
        // proposal can reach.
        // ---------------------------------------------------------------------
        group.MapPost("/policies", async (
                [FromBody] RiskPolicyUpsertRequest body,
                HttpContext httpContext,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateRiskPolicyCommand(
                    body.Name, body.MaxRiskPerTradePercent, body.MaxPositionNotional, body.MaxDailyLossPercent,
                    body.MaxPortfolioExposurePercent, body.MaxAssetConcentrationPercent, body.MaxOpenPositions,
                    body.MinConfidencePercent, body.MaxVolatilityPercent, body.RequireStopLoss,
                    body.RequireTakeProfit, body.SlippageAssumptionPercent, body.TakerFeePercent,
                    body.MarketDataStalenessThresholdSeconds, body.QuoteAsset, ActorOf(httpContext));

                var result = await sender.SendAsync(command, cancellationToken);

                return result.IsSuccess
                    ? Results.Created($"/api/v1/risk/policies/{result.Value.Id}", result.Value)
                    : ToProblem(result.Error);
            })
            .WithName("CreateRiskPolicy")
            .WithSummary("Creates a new risk policy.")
            .WithDescription(
                "The new policy is not applied anywhere until promoted with "
                + "POST /policies/{id}/mark-default or selected explicitly by id on an evaluation.")
            .Produces<RiskPolicyDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(AgentivaPolicies.CanAdminister);

        group.MapPut("/policies/{id:guid}", async (
                Guid id,
                [FromBody] RiskPolicyUpsertRequest body,
                HttpContext httpContext,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new UpdateRiskPolicyCommand(
                    id, body.MaxRiskPerTradePercent, body.MaxPositionNotional, body.MaxDailyLossPercent,
                    body.MaxPortfolioExposurePercent, body.MaxAssetConcentrationPercent, body.MaxOpenPositions,
                    body.MinConfidencePercent, body.MaxVolatilityPercent, body.RequireStopLoss,
                    body.RequireTakeProfit, body.SlippageAssumptionPercent, body.TakerFeePercent,
                    body.MarketDataStalenessThresholdSeconds, body.QuoteAsset, ActorOf(httpContext));

                var result = await sender.SendAsync(command, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("UpdateRiskPolicy")
            .WithSummary("Replaces every limit on an existing risk policy.")
            .Produces<RiskPolicyDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(AgentivaPolicies.CanAdminister);

        group.MapPost("/policies/{id:guid}/deactivate", async (
                Guid id, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(
                    new DeactivateRiskPolicyCommand(id, ActorOf(httpContext)), cancellationToken);

                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("DeactivateRiskPolicy")
            .WithSummary("Deactivates a policy so it can no longer be applied.")
            .WithDescription("Refuses to deactivate the current default — promote another policy first.")
            .Produces<RiskPolicyDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(AgentivaPolicies.CanAdminister);

        group.MapPost("/policies/{id:guid}/mark-default", async (
                Guid id, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(
                    new MarkRiskPolicyAsDefaultCommand(id, ActorOf(httpContext)), cancellationToken);

                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("MarkRiskPolicyAsDefault")
            .WithSummary("Promotes a policy to be the one applied when none is specified, demoting the previous default.")
            .Produces<RiskPolicyDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(AgentivaPolicies.CanAdminister);

        // ---------------------------------------------------------------------
        // Kill switch
        // ---------------------------------------------------------------------
        group.MapGet("/kill-switch", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetKillSwitchStatusQuery(), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetKillSwitchStatus")
            .WithSummary("Returns the current kill-switch and trading-admission state.")
            .Produces<KillSwitchStatusDto>()
            .RequireAuthorization(AgentivaPolicies.CanView);

        group.MapPost("/kill-switch/engage", async (
                [FromBody] EngageKillSwitchRequest body,
                HttpContext httpContext,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new EngageKillSwitchCommand(body.Trigger, body.Detail, ActorOf(httpContext));
                var result = await sender.SendAsync(command, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("EngageKillSwitch")
            .WithSummary("Engages the global kill switch. Every subsequent risk evaluation is rejected until released.")
            .Produces<KillSwitchStatusDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization(AgentivaPolicies.CanOperate);

        group.MapPost("/kill-switch/release", async (
                [FromBody] ReleaseKillSwitchRequest body,
                HttpContext httpContext,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new ReleaseKillSwitchCommand(ActorOf(httpContext), body.Justification);
                var result = await sender.SendAsync(command, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("ReleaseKillSwitch")
            .WithSummary("Releases the global kill switch.")
            .WithDescription(
                "If Trading:KillSwitchEnabled is still true in configuration, the response reports "
                + "engaged=true even after a successful release: configuration always wins, and a "
                + "redeploy is required to actually resume trading in that case.")
            .Produces<KillSwitchStatusDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization(AgentivaPolicies.CanOperate);

        return endpoints;
    }

    /// <summary>
    /// The authenticated caller's id, for every field that must record who
    /// acted. Never taken from the request body — a caller naming its own
    /// actor would make the audit trail worthless.
    /// </summary>
    private static string ActorOf(HttpContext httpContext)
        => httpContext.User.FindFirst("sub")?.Value ?? httpContext.User.Identity?.Name ?? "SYSTEM";

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

/// <summary>Request body for creating or replacing a risk policy's limits.</summary>
/// <param name="Name">Policy name. Ignored on update — a policy is not renamed through this endpoint.</param>
/// <param name="MaxRiskPerTradePercent">Maximum equity at risk per trade, in percent.</param>
/// <param name="MaxPositionNotional">Maximum value of one position, in the quote asset.</param>
/// <param name="MaxDailyLossPercent">Maximum loss in one UTC day, in percent of equity.</param>
/// <param name="MaxPortfolioExposurePercent">Maximum combined exposure, in percent of equity.</param>
/// <param name="MaxAssetConcentrationPercent">Maximum single-symbol exposure, in percent of equity.</param>
/// <param name="MaxOpenPositions">Maximum simultaneously open positions.</param>
/// <param name="MinConfidencePercent">Minimum signal confidence, in percent.</param>
/// <param name="MaxVolatilityPercent">Maximum acceptable symbol volatility, in percent.</param>
/// <param name="RequireStopLoss">Whether a stop-loss is mandatory.</param>
/// <param name="RequireTakeProfit">Whether a take-profit is mandatory.</param>
/// <param name="SlippageAssumptionPercent">Adverse slippage assumed per leg, in percent.</param>
/// <param name="TakerFeePercent">Taker fee assumed per leg, in percent.</param>
/// <param name="MarketDataStalenessThresholdSeconds">Maximum tolerated market data age.</param>
/// <param name="QuoteAsset">Asset <see cref="MaxPositionNotional"/> is denominated in, e.g. <c>USDT</c>.</param>
public sealed record RiskPolicyUpsertRequest(
    string Name,
    decimal MaxRiskPerTradePercent,
    decimal MaxPositionNotional,
    decimal MaxDailyLossPercent,
    decimal MaxPortfolioExposurePercent,
    decimal MaxAssetConcentrationPercent,
    int MaxOpenPositions,
    decimal MinConfidencePercent,
    decimal MaxVolatilityPercent,
    bool RequireStopLoss,
    bool RequireTakeProfit,
    decimal SlippageAssumptionPercent,
    decimal TakerFeePercent,
    double MarketDataStalenessThresholdSeconds,
    string QuoteAsset);

/// <summary>Request body to engage the kill switch.</summary>
/// <param name="Trigger">Stable trigger code, e.g. <c>operator_manual</c>.</param>
/// <param name="Detail">Human-readable context for the activation.</param>
public sealed record EngageKillSwitchRequest(string Trigger, string Detail);

/// <summary>Request body to release the kill switch.</summary>
/// <param name="Justification">Why it is now safe to resume trading. Required.</param>
public sealed record ReleaseKillSwitchRequest(string Justification);
