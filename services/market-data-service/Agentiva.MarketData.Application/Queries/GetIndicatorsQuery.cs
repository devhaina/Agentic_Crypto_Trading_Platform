using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;

namespace Agentiva.MarketData.Application.Queries;

/// <summary>Returns the most recent computed indicator values for a symbol and timeframe.</summary>
public sealed record GetIndicatorsQuery(string Symbol, string Timeframe) : IQuery<Result<IndicatorsDto>>;

/// <summary>Handles <see cref="GetIndicatorsQuery"/>.</summary>
public sealed class GetIndicatorsQueryHandler(IMarketDataReadRepository repository)
    : IRequestHandler<GetIndicatorsQuery, Result<IndicatorsDto>>
{
    public async Task<Result<IndicatorsDto>> HandleAsync(GetIndicatorsQuery request, CancellationToken cancellationToken)
    {
        var indicators = await repository.GetLatestIndicatorsAsync(request.Symbol, request.Timeframe, cancellationToken);

        return indicators is not null
            ? Result.Success(indicators)
            : Result.Failure<IndicatorsDto>(Error.NotFound(
                "market_data.indicators_unavailable",
                $"No indicators have been computed yet for {request.Symbol} on {request.Timeframe}."));
    }
}
