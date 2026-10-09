using Agentiva.Strategy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Strategy.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="Signal"/>.</summary>
public sealed class SignalConfiguration : IEntityTypeConfiguration<Signal>
{
    public void Configure(EntityTypeBuilder<Signal> builder)
    {
        builder.ToTable("signals", "strategy");
        builder.HasKey(s => s.Id);

        // Denormalised onto the row rather than joined from Strategies at read
        // time: a signal must still read correctly if the strategy it came
        // from is later renamed, and the performance endpoints read nothing
        // but this table.
        builder.Property(s => s.StrategyName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.StrategyVersion).HasMaxLength(20).IsRequired();

        builder.Property(s => s.Timeframe).HasMaxLength(8).IsRequired();
        builder.Property(s => s.Action).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.ReasonCodesJoined).HasColumnName("reason_codes").HasMaxLength(500);

        builder.Ignore(s => s.ReasonCodes);

        // Supports both "recent signals, any symbol" and "recent signals for
        // this symbol" without a second index: the leading column serves the
        // unfiltered query, the pair serves the filtered one.
        builder.HasIndex(s => s.ComputedAt).IsDescending().HasDatabaseName("ix_signals_computed_at");
        builder.HasIndex(s => new { s.Symbol, s.ComputedAt }).HasDatabaseName("ix_signals_symbol_computed_at");

        // The engine's own hot query: aggregate counts for one strategy.
        builder.HasIndex(s => s.StrategyId).HasDatabaseName("ix_signals_strategy");

        builder.Property(s => s.Version).IsRowVersion();
    }
}
