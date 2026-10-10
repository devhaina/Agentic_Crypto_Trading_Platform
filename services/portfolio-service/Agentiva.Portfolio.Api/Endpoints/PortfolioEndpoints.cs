using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.ServiceDefaults.Security;
using Agentiva.Portfolio.Application.Positions;
using Agentiva.Portfolio.Application.Snapshots;
using Microsoft.AspNetCore.Mvc;

namespace Agentiva.Portfolio.Api.Endpoints;

/// <summary>The Portfolio Service's HTTP surface.</summary>
public static class PortfolioEndpoints
{
    /// <summary>Maps the portfolio endpoints under <c>/api/v1/portfolio</c>.</summary>
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/portfolio")
            .WithTags("Portfolio");

        // ---------------------------------------------------------------------
        // GET /api/v1/portfolio/snapshot — what the Trading Service's risk
        // workflow actually calls.
        // ---------------------------------------------------------------------
        group.MapGet("/snapshot", async (
                [FromQuery] Guid tradingAccountId,
                [FromQuery] string symbol,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(
                    new GetPortfolioSnapshotQuery(tradingAccountId, symbol), cancellationToken);

                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetPortfolioSnapshot")
            .WithSummary("Returns an account's equity, available cash, exposure and today's P&L.")
            .WithDescription(
                "The real data behind Trading.Application.Abstractions.IPortfolioSnapshotProvider. "
                + "An account with no fills yet reports the configured starting-cash baseline rather "
                + "than a zeroed-out snapshot.")
            .Produces<PortfolioSnapshotResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization(AgentivaPolicies.CanView);

        // ---------------------------------------------------------------------
        // GET /api/v1/portfolio/positions — the dashboard's Positions page.
        // ---------------------------------------------------------------------
        group.MapGet("/positions", async (
                [FromQuery] Guid tradingAccountId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetPositionsQuery(tradingAccountId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetPositions")
            .WithSummary("Lists every open position for a trading account, marked against a live price.")
            .Produces<IReadOnlyList<PositionResponse>>()
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
