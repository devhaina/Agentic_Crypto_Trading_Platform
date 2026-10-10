using Agentiva.Portfolio.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Portfolio.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="PortfolioAccount"/>.</summary>
public sealed class PortfolioAccountConfiguration : IEntityTypeConfiguration<PortfolioAccount>
{
    public void Configure(EntityTypeBuilder<PortfolioAccount> builder)
    {
        builder.ToTable("accounts", "portfolio");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.QuoteAsset).HasMaxLength(12).IsRequired();
        builder.Property(a => a.Version).IsRowVersion();

        // One cash balance row per trading account.
        builder
            .HasIndex(a => a.TradingAccountId)
            .IsUnique()
            .HasDatabaseName("ux_accounts_trading_account");
    }
}
