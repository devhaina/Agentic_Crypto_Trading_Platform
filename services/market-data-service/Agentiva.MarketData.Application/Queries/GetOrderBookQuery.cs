using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;

namespace Agentiva.MarketData.Application.Queries;

/// <summary>Returns the latest order book depth for a symbol, truncated to the requested depth per side.</summary>
public sealed record GetOrderBookQuery(string Symbol, int Depth) : IQuery<Result<OrderBookDto>>;

/// <summary>Handles <see cref="GetOrderBookQuery"/>.</summary>
public sealed class GetOrderBookQueryHandler(IMarketDataCache cache, IMarketDataReadRepository repository)
    : IRequestHandler<GetOrderBookQuery, Result<OrderBookDto>>
{
    public async Task<Result<OrderBookDto>> HandleAsync(GetOrderBookQuery request, CancellationToken cancellationToken)
    {
        var book = await cache.GetLatestOrderBookAsync(request.Symbol, cancellationToken)
                   ?? await repository.GetLatestOrderBookAsync(request.Symbol, cancellationToken);

        if (book is null)
        {
            return Result.Failure<OrderBookDto>(Error.NotFound(
                "market_data.orderbook_unavailable",
                $"No order book data has been received yet for {request.Symbol}."));
        }

        var depth = Math.Max(request.Depth, 0);

        return Result.Success(book with
        {
            Bids = Truncate(book.Bids, depth),
            Asks = Truncate(book.Asks, depth)
        });
    }

    private static IReadOnlyList<PriceLevelDto> Truncate(IReadOnlyList<PriceLevelDto> levels, int depth)
        => levels.Count <= depth ? levels : levels.Take(depth).ToArray();
}
