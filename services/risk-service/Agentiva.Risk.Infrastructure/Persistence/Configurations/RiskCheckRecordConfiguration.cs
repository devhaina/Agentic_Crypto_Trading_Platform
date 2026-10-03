using Agentiva.Risk.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Risk.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="RiskCheckRecord"/>.</summary>
public sealed class RiskCheckRecordConfiguration : IEntityTypeConfiguration<RiskCheckRecord>
{
    public void Configure(EntityTypeBuilder<RiskCheckRecord> builder)
    {
        builder.ToTable("risk_checks", "risk");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.TradingMode).HasConversion<string>().HasMaxLength(20);

        builder.Property(r => r.BindingConstraint).HasMaxLength(50);
        builder.Property(r => r.RejectionCodes).HasMaxLength(2000);
        builder.Property(r => r.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.AgentRunId).HasMaxLength(100);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(200).IsRequired();

        // The complete check list as jsonb. These rows are written once and read
        // whole for display or audit, never filtered by an individual check
        // value, so normalising into a child table would add a join to the hot
        // write path for no query benefit.
        builder.Property(r => r.ChecksJson).HasColumnType("jsonb").IsRequired();

        // Supports the dashboard's "recent decisions" view.
        builder
            .HasIndex(r => r.EvaluatedAt)
            .IsDescending()
            .HasDatabaseName("ix_risk_checks_evaluated_at");

        // Supports "show me the risk decision for this intent", the first
        // question asked when reconstructing a trade.
        builder
            .HasIndex(r => r.TradingIntentId)
            .HasDatabaseName("ix_risk_checks_trading_intent");

        builder
            .HasIndex(r => r.CorrelationId)
            .HasDatabaseName("ix_risk_checks_correlation");

        // One evaluation per idempotency key: the durable backstop behind the
        // idempotency behavior.
        builder
            .HasIndex(r => r.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_risk_checks_idempotency_key");

        builder.Property(r => r.Version).IsRowVersion();
    }
}
