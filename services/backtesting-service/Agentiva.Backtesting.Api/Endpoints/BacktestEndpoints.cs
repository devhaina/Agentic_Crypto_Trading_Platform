using Agentiva.Backtesting.Application.Commands;
using Agentiva.Backtesting.Application.Contracts;
using Agentiva.Backtesting.Application.Queries;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.ServiceDefaults.Security;
using Microsoft.AspNetCore.Mvc;

namespace Agentiva.Backtesting.Api.Endpoints;

/// <summary>The Backtesting Service's HTTP surface.</summary>
/// <remarks>
/// Routed by the API gateway under <c>/backtesting</c> (see
/// <c>backtesting-read</c>/<c>backtesting-write</c> in the gateway's own
/// <c>appsettings.json</c>, reserved since Phase 1). Holds no exchange
/// credential and places no order — every run here replays historical
/// candles already in the shared market database against one of the
/// Strategy Service's own deterministic rules, in process, and records the
/// result. Nothing here can reach the Execution Service.
/// </remarks>
public static class BacktestEndpoints
{
    /// <summary>Maps the backtesting endpoints under <c>/api/v1/backtesting</c>.</summary>
    public static IEndpointRouteBuilder MapBacktestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/backtesting").WithTags("Backtesting");

        group.MapPost("/", async (
                [FromBody] RunBacktestRequest body, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new RunBacktestCommand(
                    body.Symbol, body.Timeframe, body.StrategyName, body.PeriodStart, body.PeriodEnd,
                    body.StartingCapital, body.FeePercent, body.SlippagePercent, body.RiskPerTradePercent,
                    body.WalkForwardWindowCount, body.CandleBufferCapacity);

                var result = await sender.SendAsync(command, cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("RunBacktest")
            .WithSummary("Runs one historical simulation and persists its result.")
            .WithDescription(
                "Synchronous: the response is the completed (or failed) run. A failure here means the "
                + "simulation could not run — most commonly too little historical data in the requested "
                + "period — not that the strategy lost money; a strategy that loses money is still a "
                + "completed run with a negative return.")
            .Produces<BacktestResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization(AgentivaPolicies.CanOperate);

        group.MapGet("/", async (
                ISender sender, CancellationToken cancellationToken, int limit = 50) =>
            {
                var result = await sender.SendAsync(new ListBacktestRunsQuery(limit), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("ListBacktestRuns")
            .WithSummary("Lists recent backtest runs, newest first.")
            .Produces<IReadOnlyList<BacktestSummaryDto>>()
            .RequireAuthorization(AgentivaPolicies.CanView);

        group.MapGet("/{backtestId:guid}", async (
                Guid backtestId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetBacktestRunQuery(backtestId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetBacktestRun")
            .WithSummary("Returns one backtest's full result, minus its individual trades.")
            .Produces<BacktestResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(AgentivaPolicies.CanView);

        group.MapGet("/{backtestId:guid}/trades", async (
                Guid backtestId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetBacktestTradesQuery(backtestId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetBacktestTrades")
            .WithSummary("Returns every simulated trade for one backtest.")
            .Produces<IReadOnlyList<BacktestTradeDto>>()
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

/// <summary>Request body for <c>POST /api/v1/backtests</c>.</summary>
/// <remarks>
/// <c>RiskPerTradePercent</c> is the fraction of current equity risked per
/// trade against the strategy's own ATR-derived stop. <c>WalkForwardWindowCount</c>
/// is how many sequential, non-overlapping windows the period is sliced into
/// for validation. <c>CandleBufferCapacity</c> is the rolling window size fed
/// to the strategy each bar — see <c>StrategyOptions.CandleBufferCapacity</c>
/// for the live equivalent.
/// </remarks>
public sealed record RunBacktestRequest(
    string Symbol,
    string Timeframe,
    string StrategyName,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal StartingCapital,
    decimal FeePercent,
    decimal SlippagePercent,
    decimal RiskPerTradePercent = 1m,
    int WalkForwardWindowCount = 4,
    int CandleBufferCapacity = 250);
