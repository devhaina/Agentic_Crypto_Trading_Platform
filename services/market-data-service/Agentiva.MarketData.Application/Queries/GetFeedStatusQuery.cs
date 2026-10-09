using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;

namespace Agentiva.MarketData.Application.Queries;

/// <summary>Returns live connection and freshness status for every configured symbol.</summary>
public sealed record GetFeedStatusQuery : IQuery<Result<IReadOnlyList<SymbolFeedStatusDto>>>;

/// <summary>Handles <see cref="GetFeedStatusQuery"/>.</summary>
public sealed class GetFeedStatusQueryHandler(IMarketFeedStatusProvider statusProvider)
    : IRequestHandler<GetFeedStatusQuery, Result<IReadOnlyList<SymbolFeedStatusDto>>>
{
    public Task<Result<IReadOnlyList<SymbolFeedStatusDto>>> HandleAsync(
        GetFeedStatusQuery request, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success(statusProvider.GetStatus()));
}
