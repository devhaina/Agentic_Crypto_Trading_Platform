using Agentiva.Backtesting.Application.Abstractions;
using Agentiva.Backtesting.Domain.Runs;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Backtesting.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IBacktestRunRepository"/>.</summary>
public sealed class BacktestRunRepository(BacktestingDbContext context) : IBacktestRunRepository
{
    public void Add(BacktestRun run) => context.BacktestRuns.Add(run);

    public Task<BacktestRun?> GetAsync(BacktestId id, CancellationToken cancellationToken)
        => context.BacktestRuns
            .Include(r => r.Trades)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <remarks>
    /// Includes <c>Trades</c> so <c>BacktestSummaryDto.TradeCount</c> is
    /// accurate, which loads every trade row for every listed run rather
    /// than a lighter per-run count. Acceptable while a run's trade count is
    /// in the tens to low hundreds; a dedicated count projection is the fix
    /// if that stops being true.
    /// </remarks>
    public async Task<IReadOnlyList<BacktestRun>> ListAsync(int limit, CancellationToken cancellationToken)
        => await context.BacktestRuns
            .Include(r => r.Trades)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
}
