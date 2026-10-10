using Agentiva.Backtesting.Domain.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Backtesting.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="BacktestTrade"/>.</summary>
public sealed class BacktestTradeConfiguration : IEntityTypeConfiguration<BacktestTrade>
{
    public void Configure(EntityTypeBuilder<BacktestTrade> builder)
    {
        builder.ToTable("backtest_trades", "backtesting");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.ExitReason).HasConversion<string>().HasMaxLength(20);

        builder.Property(t => t.ReasonCodesJoined).HasColumnName("reason_codes").HasMaxLength(500);
        builder.Ignore(t => t.ReasonCodes);

        // Every trade for one run, oldest first — GetBacktestTradesQuery's only access path.
        builder.HasIndex(t => new { t.BacktestId, t.EntryTime }).HasDatabaseName("ix_backtest_trades_run_entry_time");
    }
}
