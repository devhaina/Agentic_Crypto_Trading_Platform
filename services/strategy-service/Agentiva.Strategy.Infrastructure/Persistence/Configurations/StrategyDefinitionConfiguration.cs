using Agentiva.Strategy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Strategy.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="StrategyDefinition"/>.</summary>
public sealed class StrategyDefinitionConfiguration : IEntityTypeConfiguration<StrategyDefinition>
{
    public void Configure(EntityTypeBuilder<StrategyDefinition> builder)
    {
        builder.ToTable("strategies", "strategy");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500).IsRequired();
        builder.Property(s => s.CurrentVersion).HasMaxLength(20).IsRequired();

        // The engine looks a strategy up by name once per evaluation cycle.
        builder.HasIndex(s => s.Name).IsUnique().HasDatabaseName("ux_strategies_name");

        builder.Property(s => s.Version).IsRowVersion();
    }
}
