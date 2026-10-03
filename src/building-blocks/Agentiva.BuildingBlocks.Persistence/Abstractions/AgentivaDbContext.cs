using System.Reflection;
using System.Text.Json;
using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.Persistence.Conventions;
using Agentiva.BuildingBlocks.Persistence.Idempotency;
using Agentiva.BuildingBlocks.Persistence.Inbox;
using Agentiva.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.BuildingBlocks.Persistence.Abstractions;

/// <summary>
/// Base <see cref="DbContext"/> for every Agentiva service that owns a
/// relational database.
/// </summary>
/// <remarks>
/// Supplies three things each service would otherwise re-implement: the
/// financial column conventions, the outbox/inbox/idempotency tables, and an
/// override of <see cref="SaveChangesAsync(CancellationToken)"/> that makes a
/// state change and its outgoing events one atomic write.
/// </remarks>
public abstract class AgentivaDbContext(
    DbContextOptions options,
    IClock clock,
    ICorrelationContext correlation)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Events awaiting publication. Written only by this context.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Consumed event ledger, used to de-duplicate redeliveries.</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>Executed financial commands, keyed by idempotency key.</summary>
    public DbSet<IdempotencyEntry> IdempotencyEntries => Set<IdempotencyEntry>();

    /// <summary>The PostgreSQL schema this service's tables live in.</summary>
    protected abstract string Schema { get; }

    /// <summary>
    /// Persists pending changes, writing any domain events raised by tracked
    /// aggregates into the outbox in the same transaction.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Drain before saving so the outbox rows participate in the same
        // transaction as the state change. Draining afterwards would reopen the
        // dual-write gap the outbox exists to close.
        WriteDomainEventsToOutbox();

        return await base.SaveChangesAsync(cancellationToken);
    }

    private void WriteDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker
            .Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .Where(a => a.DomainEvents.Count > 0)
            .ToArray();

        if (aggregates.Length == 0)
        {
            return;
        }

        var now = clock.UtcNow;

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DrainDomainEvents())
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    EventId = domainEvent.EventId,
                    EventType = domainEvent.EventType,
                    EventVersion = domainEvent.Version,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), AgentivaJson.Options),
                    CorrelationId = correlation.CorrelationId,
                    AgentRunId = correlation.AgentRunId,
                    Status = OutboxStatus.Pending,
                    CreatedAt = now,
                    NextAttemptAt = now
                });
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        ConfigureOutbox(modelBuilder);
        ConfigureInbox(modelBuilder);
        ConfigureIdempotency(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // --- The financial precision rule, applied globally -------------------
        // NUMERIC(38,18) for every decimal. Set as a convention rather than
        // per-property so that a newly added money column cannot silently
        // default to NUMERIC(18,2) and start truncating satoshi-level
        // quantities. 18 decimal places covers the smallest unit of every asset
        // the platform trades, including 18-decimal ERC-20 tokens.
        configurationBuilder.Properties<decimal>().HavePrecision(38, 18);

        // Timestamps are always instants with an offset, stored as timestamptz.
        // A naive timestamp column loses the offset and makes "which trading
        // day" ambiguous across deployments in different regions.
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamptz");

        // --- Value object conventions -----------------------------------------
        configurationBuilder.Properties<Symbol>().HaveConversion<SymbolConverter>().HaveMaxLength(24);
        configurationBuilder.Properties<AssetCode>().HaveConversion<AssetCodeConverter>().HaveMaxLength(12);
        configurationBuilder.Properties<Quantity>().HaveConversion<QuantityConverter>().HavePrecision(38, 18);
        configurationBuilder.Properties<Price>().HaveConversion<PriceConverter>().HavePrecision(38, 18);
        configurationBuilder.Properties<Percentage>().HaveConversion<PercentageConverter>().HavePrecision(38, 18);

        RegisterStronglyTypedIdConversions(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);
    }

    /// <summary>
    /// Registers a GUID conversion for every strongly typed identifier declared
    /// in the Domain building block.
    /// </summary>
    /// <remarks>
    /// Discovered by reflection rather than listed by hand: a new identifier
    /// type should not require a matching edit in every service's DbContext,
    /// and a forgotten registration surfaces as an obscure EF mapping error.
    /// </remarks>
    private static void RegisterStronglyTypedIdConversions(ModelConfigurationBuilder configurationBuilder)
    {
        var idTypes = typeof(OrderId).Assembly
            .GetTypes()
            .Where(t => t is { IsValueType: true, IsGenericTypeDefinition: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStronglyTypedId<>)));

        foreach (var idType in idTypes)
        {
            var converterType = typeof(StronglyTypedIdConverter<>).MakeGenericType(idType);
            configurationBuilder.Properties(idType).HaveConversion(converterType);
        }
    }

    private void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(builder =>
        {
            builder.ToTable("outbox_messages", Schema);
            builder.HasKey(m => m.Id);

            builder.Property(m => m.EventType).HasMaxLength(200).IsRequired();
            builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
            builder.Property(m => m.CorrelationId).HasMaxLength(100).IsRequired();
            builder.Property(m => m.AgentRunId).HasMaxLength(100);
            builder.Property(m => m.LastError).HasMaxLength(2000);
            builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);

            // The processor's only hot query: pending rows whose backoff has
            // elapsed, oldest first. Filtered so the index stays small — it
            // holds the unpublished backlog, not the full event history.
            builder
                .HasIndex(m => new { m.Status, m.NextAttemptAt })
                .HasDatabaseName("ix_outbox_pending")
                .HasFilter("status = 'Pending'");

            // Guards against the same domain event being enqueued twice, which
            // would otherwise be possible if a retry re-ran a handler.
            builder.HasIndex(m => m.EventId).IsUnique().HasDatabaseName("ux_outbox_event_id");
        });
    }

    private void ConfigureInbox(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessage>(builder =>
        {
            builder.ToTable("inbox_messages", Schema);
            builder.HasKey(m => m.Id);

            builder.Property(m => m.ConsumerName).HasMaxLength(300).IsRequired();
            builder.Property(m => m.EventType).HasMaxLength(200).IsRequired();

            // The de-duplication guarantee. Unique on the pair, because several
            // handlers in one service may each need to process the same event.
            builder
                .HasIndex(m => new { m.EventId, m.ConsumerName })
                .IsUnique()
                .HasDatabaseName("ux_inbox_event_consumer");
        });
    }

    private void ConfigureIdempotency(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyEntry>(builder =>
        {
            builder.ToTable("idempotency_entries", Schema);
            builder.HasKey(e => e.Id);

            builder.Property(e => e.Key).HasMaxLength(200).IsRequired();
            builder.Property(e => e.RequestType).HasMaxLength(300).IsRequired();
            builder.Property(e => e.ResponsePayload).HasColumnType("jsonb");
            builder.Property(e => e.FailureReason).HasMaxLength(2000);
            builder.Property(e => e.State).HasConversion<string>().HasMaxLength(20);

            // The concurrency control for duplicate financial commands: the
            // database, not application logic, decides which caller wins.
            builder.HasIndex(e => e.Key).IsUnique().HasDatabaseName("ux_idempotency_key");
        });
    }

    /// <summary>Applies every <c>IEntityTypeConfiguration</c> in the given assembly.</summary>
    protected void ApplyConfigurationsFrom(ModelBuilder modelBuilder, Assembly assembly)
        => modelBuilder.ApplyConfigurationsFromAssembly(assembly);
}
