using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.Strategy.Application.Abstractions;
using Agentiva.Strategy.Application.Contracts;

namespace Agentiva.Strategy.Application.Queries;

/// <summary>Returns the most recent signals, newest first, optionally filtered to one symbol.</summary>
public sealed record GetRecentSignalsQuery(string? Symbol, int Limit) : IQuery<Result<IReadOnlyList<SignalDto>>>;

/// <summary>Handles <see cref="GetRecentSignalsQuery"/>.</summary>
public sealed class GetRecentSignalsQueryHandler(ISignalRepository signals)
    : IRequestHandler<GetRecentSignalsQuery, Result<IReadOnlyList<SignalDto>>>
{
    public async Task<Result<IReadOnlyList<SignalDto>>> HandleAsync(
        GetRecentSignalsQuery request, CancellationToken cancellationToken)
    {
        var results = await signals.ListRecentAsync(request.Symbol, request.Limit, cancellationToken);
        return Result.Success(results);
    }
}
