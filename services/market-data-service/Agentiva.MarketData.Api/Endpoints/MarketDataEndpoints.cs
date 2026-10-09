using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.ServiceDefaults.Security;
using Agentiva.MarketData.Application.Contracts;
using Agentiva.MarketData.Application.Queries;

namespace Agentiva.MarketData.Api.Endpoints;

/// <summary>The Market Data Service's HTTP surface.</summary>
/// <remarks>
/// Routed by the API gateway under <c>/market</c> — see the
/// <c>market-read</c>/<c>market-write</c> clusters in the gateway's
/// <c>appsettings.json</c> — and consumed both directly and by the AI agent
/// platform's <c>get_market_data</c>, <c>get_candles</c>, <c>get_orderbook</c> and
/// <c>get_indicators</c> tools in <c>ai/agent-platform/agentiva_agents/tools/market_tools.py</c>.
/// Every route here is a read; there is deliberately no write surface, because
/// nothing outside this service's own ingestion pipeline ever originates
/// market data.
/// </remarks>
public static class MarketDataEndpoints
{
    /// <summary>Maps the market data endpoints under <c>/api/v1/market</c>.</summary>
    public static IEndpointRouteBuilder MapMarketDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/market")
            .WithTags("MarketData")
            .RequireAuthorization(AgentivaPolicies.CanView);

        group.MapGet("/symbols/{symbol}/ticker", async (
                string symbol, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetTickerQuery(symbol), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetTicker")
            .WithSummary("Returns the latest bid/ask/last quote for a symbol.")
            .Produces<TickerDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/symbols/{symbol}/candles", async (
                string symbol,
                ISender sender,
                CancellationToken cancellationToken,
                string timeframe = "15m",
                int limit = 200) =>
            {
                var result = await sender.SendAsync(new GetCandlesQuery(symbol, timeframe, limit), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetCandles")
            .WithSummary("Returns recent closed candles for a symbol and timeframe, newest first.")
            .Produces<IReadOnlyList<CandleDto>>();

        group.MapGet("/symbols/{symbol}/orderbook", async (
                string symbol,
                ISender sender,
                CancellationToken cancellationToken,
                int depth = 20) =>
            {
                var result = await sender.SendAsync(new GetOrderBookQuery(symbol, depth), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetOrderBook")
            .WithSummary("Returns the latest order book depth for a symbol.")
            .Produces<OrderBookDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/symbols/{symbol}/indicators", async (
                string symbol,
                ISender sender,
                CancellationToken cancellationToken,
                string timeframe = "15m") =>
            {
                var result = await sender.SendAsync(new GetIndicatorsQuery(symbol, timeframe), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetIndicators")
            .WithSummary("Returns the most recent computed indicator values for a symbol and timeframe.")
            .WithDescription(
                "Reads indicator_snapshots, written by the Strategy Service — see that service's "
                + "Phase 3 indicator engine. 404 until at least one snapshot has been computed for "
                + "the requested symbol and timeframe.")
            .Produces<IndicatorsDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/status", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.SendAsync(new GetFeedStatusQuery(), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : ToProblem(result.Error);
            })
            .WithName("GetMarketFeedStatus")
            .WithSummary("Returns live connection and freshness status for every configured symbol.")
            .Produces<IReadOnlyList<SymbolFeedStatusDto>>();

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
