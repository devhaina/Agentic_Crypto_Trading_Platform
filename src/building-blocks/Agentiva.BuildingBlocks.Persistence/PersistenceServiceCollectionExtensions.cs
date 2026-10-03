using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Agentiva.BuildingBlocks.Persistence.Idempotency;
using Agentiva.BuildingBlocks.Persistence.Inbox;
using Agentiva.BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agentiva.BuildingBlocks.Persistence;

/// <summary>Registration helpers for an Agentiva service database.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers a service's <see cref="DbContext"/> with the platform's
    /// PostgreSQL conventions, plus the inbox, idempotency store and unit of work.
    /// </summary>
    /// <typeparam name="TContext">The service's context type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <param name="connectionStringName">Name of the connection string to use.</param>
    public static IServiceCollection AddAgentivaDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "Postgres")
        where TContext : AgentivaDbContext
    {
        var connectionString = configuration.GetConnectionString(connectionStringName)
                               ?? throw new InvalidOperationException(
                                   $"Connection string '{connectionStringName}' is not configured.");

        services.AddDbContext<TContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                // Transient faults — a failover, a dropped connection during a
                // rolling restart — are retried in the provider rather than
                // surfacing as a failed trade. Code that opens an explicit
                // transaction must wrap it in Database.CreateExecutionStrategy().
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);

                npgsql.CommandTimeout(30);

                // Keep each service's migration history in its own schema, so
                // one database can host several services during local
                // development without their histories colliding.
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
            });

            // snake_case everywhere: it is the PostgreSQL convention, it keeps
            // hand-written SQL (the outbox claim query, operational runbooks)
            // free of quoted PascalCase identifiers, and it avoids the
            // case-folding surprises that unquoted mixed-case names produce.
            options.UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IInboxStore, EfInboxStore<TContext>>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore<TContext>>();

        return services;
    }

    /// <summary>
    /// Starts the outbox processor for a context, which publishes pending events.
    /// </summary>
    /// <typeparam name="TContext">The service's context type.</typeparam>
    public static IServiceCollection AddAgentivaOutboxProcessor<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : AgentivaDbContext
    {
        services
            .AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHostedService<OutboxProcessor<TContext>>();
        return services;
    }

    /// <summary>
    /// Applies pending EF Core migrations at startup, when enabled.
    /// </summary>
    /// <typeparam name="TContext">The service's context type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Configuration root, read for the <c>Migrator:Enabled</c> flag.
    /// </param>
    /// <remarks>
    /// Enabled by default, which is what makes an unattended
    /// <c>docker compose up</c> produce a working database with no manual
    /// step. Set <c>Migrator__Enabled=false</c> in any environment where
    /// several replicas could start concurrently — several racing to migrate
    /// the same database is a recipe for a locked or half-migrated schema —
    /// and apply the migration as a separate, reviewable pipeline step
    /// instead. See <c>docs/operations/migrations.md</c> and
    /// <see cref="DatabaseMigratorOptions"/>.
    /// </remarks>
    public static IServiceCollection AddAgentivaDatabaseMigrator<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : AgentivaDbContext
    {
        services
            .AddOptions<DatabaseMigratorOptions>()
            .Bind(configuration.GetSection(DatabaseMigratorOptions.SectionName))
            .ValidateOnStart();

        services.AddHostedService<DatabaseMigrator<TContext>>();
        return services;
    }
}
