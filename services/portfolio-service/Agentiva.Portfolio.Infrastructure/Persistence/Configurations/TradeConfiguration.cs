using Agentiva.Portfolio.Domain.Trades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Portfolio.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="Trade"/>.</summary>
public sealed class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("trades", "portfolio");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.QuoteAsset).HasMaxLength(12).IsRequired();

        // The daily and lifetime P&L queries' hot index: every trade this
        // account closed at or after some instant.
        builder
            .HasIndex(t => new { t.TradingAccountId, t.ClosedAt })
            .HasDatabaseName("ix_trades_account_closed_at");
    }
}
