using Agentiva.Risk.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Risk.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="RiskPolicy"/>.</summary>
public sealed class RiskPolicyConfiguration : IEntityTypeConfiguration<RiskPolicy>
{
    public void Configure(EntityTypeBuilder<RiskPolicy> builder)
    {
        builder.ToTable("risk_policies", "risk");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.UpdatedBy).HasMaxLength(100).IsRequired();

        // Optimistic concurrency via the PostgreSQL xmin system column. Two
        // administrators editing the same policy concurrently must not have one
        // silently overwrite the other's limits.
        builder.Property(p => p.Version).IsRowVersion();

        // Percentage and Money are converted by the global conventions in
        // AgentivaDbContext; Money needs its two components split out.
        builder.Property(p => p.MaxRiskPerTrade).HasColumnName("max_risk_per_trade_percent");
        builder.Property(p => p.MaxDailyLoss).HasColumnName("max_daily_loss_percent");
        builder.Property(p => p.MaxPortfolioExposure).HasColumnName("max_portfolio_exposure_percent");
        builder.Property(p => p.MaxAssetConcentration).HasColumnName("max_asset_concentration_percent");
        builder.Property(p => p.MinConfidence).HasColumnName("min_confidence_percent");
        builder.Property(p => p.MaxVolatility).HasColumnName("max_volatility_percent");
        builder.Property(p => p.SlippageAssumption).HasColumnName("slippage_assumption_percent");
        builder.Property(p => p.TakerFee).HasColumnName("taker_fee_percent");

        builder.ComplexProperty(p => p.MaxPositionNotional, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("max_position_notional")
                .HasPrecision(38, 18);

            money.Property(m => m.Currency)
                .HasColumnName("max_position_notional_asset")
                .HasMaxLength(12);
        });

        builder.Property(p => p.MarketDataStalenessThreshold)
            .HasColumnName("market_data_staleness_threshold");

        // At most one default policy. Enforced by a partial unique index rather
        // than application logic, so two concurrent promotions cannot both
        // succeed and leave the platform with an ambiguous default.
        builder
            .HasIndex(p => p.IsDefault)
            .IsUnique()
            .HasFilter("is_default = true")
            .HasDatabaseName("ux_risk_policies_single_default");

        builder.HasIndex(p => p.Name).IsUnique().HasDatabaseName("ux_risk_policies_name");
    }
}
