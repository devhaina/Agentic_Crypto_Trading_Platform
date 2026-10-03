using Agentiva.Trading.Domain.Intents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Trading.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="TradingIntent"/>.</summary>
public sealed class TradingIntentConfiguration : IEntityTypeConfiguration<TradingIntent>
{
    public void Configure(EntityTypeBuilder<TradingIntent> builder)
    {
        builder.ToTable("trading_intents", "trading");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(i => i.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(i => i.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.TradingMode).HasConversion<string>().HasMaxLength(20);

        builder.Property(i => i.RejectionCodes).HasMaxLength(2000);
        builder.Property(i => i.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(i => i.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(i => i.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(i => i.FailureReason).HasMaxLength(1000);

        builder.Property(i => i.Confidence).HasColumnName("confidence_percent");

        // Optimistic concurrency. The workflow advances this aggregate from more
        // than one place — the API, and from Phase 5 an order-event consumer —
        // so a lost update here could overwrite a risk decision with a stale one.
        builder.Property(i => i.Version).IsRowVersion();

        // One intent per idempotency key. The durable backstop behind the
        // idempotency behavior: even if the in-flight guard were bypassed, the
        // database refuses a second intent for the same key.
        builder
            .HasIndex(i => i.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_trading_intents_idempotency_key");

        // Supports the dashboard's recent-intents view.
        builder
            .HasIndex(i => i.CreatedAt)
            .IsDescending()
            .HasDatabaseName("ix_trading_intents_created_at");

        // Supports "what is still awaiting a decision?", used by operators and
        // by the Phase 6 reconciliation sweep that resolves stuck intents.
        builder
            .HasIndex(i => new { i.Status, i.CreatedAt })
            .HasDatabaseName("ix_trading_intents_status");

        builder
            .HasIndex(i => i.CorrelationId)
            .HasDatabaseName("ix_trading_intents_correlation");

        builder
            .HasIndex(i => new { i.Symbol, i.CreatedAt })
            .HasDatabaseName("ix_trading_intents_symbol");
    }
}
