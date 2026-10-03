using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Agentiva.Trading.Domain.Intents;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Trading.Infrastructure.Persistence;

/// <summary>The Trading Service's database. Owns <c>trading_db</c>.</summary>
public sealed class TradingDbContext(
    DbContextOptions<TradingDbContext> options,
    IClock clock,
    ICorrelationContext correlation)
    : AgentivaDbContext(options, clock, correlation)
{
    protected override string Schema => "trading";

    /// <summary>Trading intents and their workflow state.</summary>
    public DbSet<TradingIntent> TradingIntents => Set<TradingIntent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsFrom(modelBuilder, typeof(TradingDbContext).Assembly);
    }
}
