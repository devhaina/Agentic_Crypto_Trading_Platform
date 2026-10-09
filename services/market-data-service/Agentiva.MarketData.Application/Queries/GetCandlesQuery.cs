using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;

namespace Agentiva.MarketData.Application.Queries;

/// <summary>Returns recent closed candles for a symbol and timeframe, newest first.</summary>
public sealed record GetCandlesQuery(string Symbol, string Timeframe, int Limit)
    : IQuery<Result<IReadOnlyList<CandleDto>>>;

/// <summary>Handles <see cref="GetCandlesQuery"/>.</summary>
/// <remarks>
/// Always reads the hypertable rather than the latest-value cache: a client
/// asking for candles wants a series, and the cache holds only the single most
/// recent one.
/// </remarks>
public sealed class GetCandlesQueryHandler(IMarketDataReadRepository repository)
    : IRequestHandler<GetCandlesQuery, Result<IReadOnlyList<CandleDto>>>
{
    public async Task<Result<IReadOnlyList<CandleDto>>> HandleAsync(
        GetCandlesQuery request, CancellationToken cancellationToken)
    {
        var candles = await repository.GetRecentCandlesAsync(
            request.Symbol, request.Timeframe, request.Limit, cancellationToken);

        return Result.Success(candles);
    }
}
