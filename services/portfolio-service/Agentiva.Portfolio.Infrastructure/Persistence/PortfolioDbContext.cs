using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Agentiva.Portfolio.Domain.Accounts;
using Agentiva.Portfolio.Domain.Positions;
using Agentiva.Portfolio.Domain.Trades;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Portfolio.Infrastructure.Persistence;

/// <summary>
/// The Portfolio Service's database. Owns <c>portfolio_db</c> and is queried
/// by no other service directly — the Trading Service's portfolio snapshot
/// and the dashboard's positions view both go through this service's own
/// HTTP API.
/// </summary>
public sealed class PortfolioDbContext(
    DbContextOptions<PortfolioDbContext> options,
    IClock clock,
    ICorrelationContext correlation)
    : AgentivaDbContext(options, clock, correlation)
{
    protected override string Schema => "portfolio";

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<PortfolioAccount> Accounts => Set<PortfolioAccount>();

    public DbSet<Trade> Trades => Set<Trade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsFrom(modelBuilder, typeof(PortfolioDbContext).Assembly);
    }
}
