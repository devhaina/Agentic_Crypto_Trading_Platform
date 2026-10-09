using Agentiva.Execution.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agentiva.Execution.Infrastructure.Persistence.Configurations;

/// <summary>EF mapping for <see cref="Order"/>.</summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "execution");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Side).HasConversion<string>().HasMaxLength(10);
        builder.Property(o => o.OrderType).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.TradingMode).HasConversion<string>().HasMaxLength(20);

        builder.Property(o => o.ClientOrderId).HasMaxLength(36).IsRequired();
        builder.Property(o => o.ExchangeOrderId).HasMaxLength(64);
        builder.Property(o => o.FeeAsset).HasMaxLength(12);
        builder.Property(o => o.RejectionCode).HasMaxLength(200);
        builder.Property(o => o.RejectionDetail).HasMaxLength(2000);

        // Optimistic concurrency: a fill can arrive while an operator cancels
        // the same order, and the second writer must retry against fresh
        // state rather than silently overwrite the first.
        builder.Property(o => o.Version).IsRowVersion();

        // The durable backstop behind the deterministic client order id: even
        // if the idempotency store and the id's own determinism were both
        // bypassed, the database refuses a second order for the same key.
        builder
            .HasIndex(o => o.ClientOrderId)
            .IsUnique()
            .HasDatabaseName("ux_orders_client_order_id");

        // The real duplicate-order check's hot query: is anything open on
        // this symbol and side right now? Filtered to the open statuses so
        // the index stays small as the order history grows.
        builder
            .HasIndex(o => new { o.Symbol, o.Side, o.Status })
            .HasDatabaseName("ix_orders_open_by_symbol_side")
            .HasFilter("status IN ('Created', 'Submitted', 'PartiallyFilled')");

        builder
            .HasIndex(o => o.TradingIntentId)
            .HasDatabaseName("ix_orders_trading_intent");

        // Supports the dashboard's recent-orders view.
        builder
            .HasIndex(o => o.CreatedAt)
            .IsDescending()
            .HasDatabaseName("ix_orders_created_at");
    }
}
