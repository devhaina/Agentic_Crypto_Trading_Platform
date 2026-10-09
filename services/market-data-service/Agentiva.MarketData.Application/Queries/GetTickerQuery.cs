using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;

namespace Agentiva.MarketData.Application.Queries;

/// <summary>Returns the latest ticker for a symbol.</summary>
public sealed record GetTickerQuery(string Symbol) : IQuery<Result<TickerDto>>;

/// <summary>Handles <see cref="GetTickerQuery"/>.</summary>
public sealed class GetTickerQueryHandler(IMarketDataCache cache, IMarketDataReadRepository repository)
    : IRequestHandler<GetTickerQuery, Result<TickerDto>>
{
    public async Task<Result<TickerDto>> HandleAsync(GetTickerQuery request, CancellationToken cancellationToken)
    {
        // The cache first: it is populated on every tick and answers in a
        // round trip to Redis rather than a hypertable scan. Only a cache
        // miss — a fresh deploy, or an expired entry — falls back to the
        // most recent row on disk.
        var cached = await cache.GetLatestTickerAsync(request.Symbol, cancellationToken);
        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var stored = await repository.GetLatestTickAsync(request.Symbol, cancellationToken);

        return stored is not null
            ? Result.Success(stored)
            : Result.Failure<TickerDto>(Error.NotFound(
                "market_data.ticker_unavailable",
                $"No ticker data has been received yet for {request.Symbol}."));
    }
}
