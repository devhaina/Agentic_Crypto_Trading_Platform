using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.ServiceDefaults.Security;
using Agentiva.Strategy.Application.Contracts;
using Agentiva.Strategy.Application.Queries;

namespace Agentiva.Strategy.Api.Endpoints;

/// <summary>The Strategy Service's HTTP surface.</summary>
/// <remarks>
/// Routed by the API gateway under <c>/strategies</c>. Read-only: nothing here
/// lets a caller create a signal or change a strategy's logic — the engine
/// is driven entirely by consumed market data, not by requests. Consumed both
/// directly and by the AI agent platform's <c>get_strategy_performance</c> and
/// <c>get_recent_signals</c> tools.
/// </remarks>
public static class StrategyEndpoints
{
    /// <summary>Maps the strategy endpoints under <c>/api/v1/strategies</c>.</summary>
    public static IEndpointRouteBuilder MapStrategyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/strategies")
            .WithTags("Strategy")
            .RequireAuthorization(AgentivaPolicies.CanView);

        group.MapGet("/performance", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetAllStrategyPerformanceQuery(), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetAllStrategyPerformance")
            .WithSummary("Returns descriptive signal counts for every strategy.")
            .WithDescription(
                "Counts only — total, buy and sell signals produced, and the first and last signal "
                + "time. Not a win rate or any other outcome-based metric: those require a filled "
                + "order (Phase 5) and a realised P&L (Phase 6/8), neither of which exist yet.")
            .Produces<IReadOnlyList<StrategyPerformanceDto>>();

        group.MapGet("/{strategyId:guid}/performance", async (
                Guid strategyId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetStrategyPerformanceQuery(strategyId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetStrategyPerformance")
            .WithSummary("Returns descriptive signal counts for one strategy.")
            .Produces<StrategyPerformanceDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/signals", async (
                ISender sender,
                CancellationToken cancellationToken,
                string? symbol = null,
                int limit = 20) =>
            {
                var result = await sender.SendAsync(new GetRecentSignalsQuery(symbol, limit), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetRecentSignals")
            .WithSummary("Returns recent deterministic strategy signals, newest first.")
            .Produces<IReadOnlyList<SignalDto>>();

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
