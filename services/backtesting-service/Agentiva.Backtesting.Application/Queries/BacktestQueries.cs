using Agentiva.Backtesting.Application.Abstractions;
using Agentiva.Backtesting.Application.Contracts;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Backtesting.Application.Queries;

/// <summary>Returns one backtest's full result, minus its individual trades.</summary>
public sealed record GetBacktestRunQuery(Guid BacktestId) : IQuery<Result<BacktestResultDto>>;

/// <summary>Lists recent backtest runs, newest first.</summary>
public sealed record ListBacktestRunsQuery(int Limit = 50) : IQuery<Result<IReadOnlyList<BacktestSummaryDto>>>;

/// <summary>Returns every simulated trade for one backtest.</summary>
public sealed record GetBacktestTradesQuery(Guid BacktestId) : IQuery<Result<IReadOnlyList<BacktestTradeDto>>>;

/// <summary>Handles <see cref="GetBacktestRunQuery"/>.</summary>
public sealed class GetBacktestRunQueryHandler(IBacktestRunRepository runs)
    : IRequestHandler<GetBacktestRunQuery, Result<BacktestResultDto>>
{
    public async Task<Result<BacktestResultDto>> HandleAsync(GetBacktestRunQuery query, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(BacktestId.From(query.BacktestId), cancellationToken);

        return run is null
            ? Error.NotFound("backtest.not_found", $"No backtest with id {query.BacktestId}.")
            : Result.Success(BacktestMapper.ToResultDto(run));
    }
}

/// <summary>Handles <see cref="ListBacktestRunsQuery"/>.</summary>
public sealed class ListBacktestRunsQueryHandler(IBacktestRunRepository runs)
    : IRequestHandler<ListBacktestRunsQuery, Result<IReadOnlyList<BacktestSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<BacktestSummaryDto>>> HandleAsync(
        ListBacktestRunsQuery query, CancellationToken cancellationToken)
    {
        var all = await runs.ListAsync(Math.Clamp(query.Limit, 1, 200), cancellationToken);

        return Result.Success<IReadOnlyList<BacktestSummaryDto>>(all.Select(BacktestMapper.ToSummaryDto).ToArray());
    }
}

/// <summary>Handles <see cref="GetBacktestTradesQuery"/>.</summary>
public sealed class GetBacktestTradesQueryHandler(IBacktestRunRepository runs)
    : IRequestHandler<GetBacktestTradesQuery, Result<IReadOnlyList<BacktestTradeDto>>>
{
    public async Task<Result<IReadOnlyList<BacktestTradeDto>>> HandleAsync(
        GetBacktestTradesQuery query, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(BacktestId.From(query.BacktestId), cancellationToken);

        if (run is null)
        {
            return Error.NotFound("backtest.not_found", $"No backtest with id {query.BacktestId}.");
        }

        return Result.Success<IReadOnlyList<BacktestTradeDto>>(run.Trades.Select(BacktestMapper.ToTradeDto).ToArray());
    }
}
