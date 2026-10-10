using Agentiva.Backtesting.Domain.Runs;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Backtesting.Infrastructure.Persistence;

/// <summary>
/// The Backtesting Service's database. Owns <c>backtesting_db</c>. The
/// historical candles a run replays are read from the shared
/// <c>market_db</c> instead — see <c>HistoricalCandleReader</c> — never
/// written here.
/// </summary>
public sealed class BacktestingDbContext(
    DbContextOptions<BacktestingDbContext> options,
    IClock clock,
    ICorrelationContext correlation)
    : AgentivaDbContext(options, clock, correlation)
{
    protected override string Schema => "backtesting";

    public DbSet<BacktestRun> BacktestRuns => Set<BacktestRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsFrom(modelBuilder, typeof(BacktestingDbContext).Assembly);
    }
}
