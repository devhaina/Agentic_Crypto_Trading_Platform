using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Strategy.Application.Abstractions;
using Agentiva.Strategy.Application.Contracts;
using Agentiva.Strategy.Domain.Entities;

namespace Agentiva.Strategy.Application.Queries;

/// <summary>Returns descriptive signal counts for every strategy.</summary>
public sealed record GetAllStrategyPerformanceQuery : IQuery<Result<IReadOnlyList<StrategyPerformanceDto>>>;

/// <summary>Returns descriptive signal counts for one strategy.</summary>
public sealed record GetStrategyPerformanceQuery(Guid StrategyId) : IQuery<Result<StrategyPerformanceDto>>;

/// <summary>Handles <see cref="GetAllStrategyPerformanceQuery"/>.</summary>
public sealed class GetAllStrategyPerformanceQueryHandler(
    IStrategyDefinitionRepository strategies, ISignalRepository signals)
    : IRequestHandler<GetAllStrategyPerformanceQuery, Result<IReadOnlyList<StrategyPerformanceDto>>>
{
    public async Task<Result<IReadOnlyList<StrategyPerformanceDto>>> HandleAsync(
        GetAllStrategyPerformanceQuery request, CancellationToken cancellationToken)
    {
        var all = await strategies.ListAllAsync(cancellationToken);

        var results = new List<StrategyPerformanceDto>(all.Count);
        foreach (var strategy in all)
        {
            results.Add(await BuildDtoAsync(strategy, signals, cancellationToken));
        }

        return Result.Success<IReadOnlyList<StrategyPerformanceDto>>(results);
    }

    internal static async Task<StrategyPerformanceDto> BuildDtoAsync(
        StrategyDefinition strategy, ISignalRepository signals, CancellationToken cancellationToken)
    {
        var (total, buys, sells, first, last) = await signals.GetCountsAsync(strategy.Id, cancellationToken);

        return new StrategyPerformanceDto(
            strategy.Id.Value, strategy.Name, strategy.CurrentVersion, strategy.IsActive,
            total, buys, sells, first, last);
    }
}

/// <summary>Handles <see cref="GetStrategyPerformanceQuery"/>.</summary>
public sealed class GetStrategyPerformanceQueryHandler(
    IStrategyDefinitionRepository strategies, ISignalRepository signals)
    : IRequestHandler<GetStrategyPerformanceQuery, Result<StrategyPerformanceDto>>
{
    public async Task<Result<StrategyPerformanceDto>> HandleAsync(
        GetStrategyPerformanceQuery request, CancellationToken cancellationToken)
    {
        var strategy = await strategies.GetByIdAsync(StrategyId.From(request.StrategyId), cancellationToken);

        if (strategy is null)
        {
            return Result.Failure<StrategyPerformanceDto>(Error.NotFound(
                "strategy.not_found", $"No strategy with id {request.StrategyId} is configured."));
        }

        var dto = await GetAllStrategyPerformanceQueryHandler.BuildDtoAsync(strategy, signals, cancellationToken);
        return Result.Success(dto);
    }
}
