using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Agentiva.Strategy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Strategy.Infrastructure.Persistence;

/// <summary>
/// The Strategy Service's database. Owns <c>strategy_db</c> and is queried by
/// no other service.
/// </summary>
/// <remarks>
/// Holds strategy identity/versioning and the signal history — the relational
/// side of this service. The time-series side, <c>indicator_snapshots</c>, is
/// a TimescaleDB hypertable written through raw Npgsql instead; see
/// <c>IndicatorSnapshotWriter</c> for why EF cannot own that table.
/// </remarks>
public sealed class StrategyDbContext(
    DbContextOptions<StrategyDbContext> options,
    IClock clock,
    ICorrelationContext correlation)
    : AgentivaDbContext(options, clock, correlation)
{
    protected override string Schema => "strategy";

    public DbSet<StrategyDefinition> Strategies => Set<StrategyDefinition>();

    public DbSet<Signal> Signals => Set<Signal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsFrom(modelBuilder, typeof(StrategyDbContext).Assembly);
    }
}
