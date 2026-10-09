using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Agentiva.Execution.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Execution.Infrastructure.Persistence;

/// <summary>
/// The Execution Service's database. Owns <c>execution_db</c> and is queried
/// by no other service directly — the Trading Service's duplicate-order
/// check and order lookups go through this service's own HTTP API.
/// </summary>
public sealed class ExecutionDbContext(
    DbContextOptions<ExecutionDbContext> options,
    IClock clock,
    ICorrelationContext correlation)
    : AgentivaDbContext(options, clock, correlation)
{
    protected override string Schema => "execution";

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsFrom(modelBuilder, typeof(ExecutionDbContext).Assembly);
    }
}
