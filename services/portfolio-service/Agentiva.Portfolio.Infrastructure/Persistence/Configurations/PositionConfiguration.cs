using Agentiva.Portfolio.Domain.Positions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Portfolio.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="Position"/>.</summary>
public sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions", "portfolio");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.OpeningSide).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.QuoteAsset).HasMaxLength(12).IsRequired();

        // Optimistic concurrency: two fills for the same symbol could in
        // principle be processed concurrently by two consumer instances, and
        // the second writer must retry against fresh state rather than
        // silently overwrite the first's P&L.
        builder.Property(p => p.Version).IsRowVersion();

        // One position per account per symbol. The event consumer's hot
        // query — "get or create the position for this fill" — and the
        // durable backstop against ever double-creating one.
        builder
            .HasIndex(p => new { p.TradingAccountId, p.Symbol })
            .IsUnique()
            .HasDatabaseName("ux_positions_account_symbol");

        // The dashboard's and the snapshot query's hot query: every open
        // position for an account. Filtered so the index stays small as
        // closed, flat positions accumulate history.
        builder
            .HasIndex(p => new { p.TradingAccountId, p.Direction })
            .HasDatabaseName("ix_positions_account_open")
            .HasFilter("direction <> 'Flat'");
    }
}
