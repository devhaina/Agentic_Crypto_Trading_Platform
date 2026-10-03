using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.BuildingBlocks.Persistence;

/// <summary>Tuning for the automatic-migration hosted service.</summary>
public sealed class DatabaseMigratorOptions
{
    public const string SectionName = "Migrator";

    /// <summary>
    /// Whether this service applies pending migrations automatically at startup.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c>, which is what makes an unattended
    /// <c>docker compose up</c> produce a working schema with no manual step —
    /// see <c>docs/operations/migrations.md</c>.
    /// </remarks>
    /// <para>
    /// Set to <c>false</c> in any environment where several replicas could
    /// start concurrently: the migrator is a required <see cref="IHostedService"/>,
    /// so if the database is unreachable at startup it throws and the whole
    /// host fails to start, by design — a service must not pretend to be
    /// healthy when it could not even confirm its schema. That fail-fast
    /// behaviour is correct for a single-replica developer stack with
    /// Compose's <c>depends_on: condition: service_healthy</c> already
    /// sequencing startup. It is wrong for a multi-replica Kubernetes
    /// deployment, where several pods starting at once would each try to
    /// apply the same migration, and where the right place to apply one is a
    /// dedicated migration <c>Job</c> run once, before any replica is
    /// scheduled.
    /// </para>
    public bool Enabled { get; set; } = true;
}

/// <summary>Applies pending migrations once at startup, when enabled.</summary>
/// <typeparam name="TContext">The service's context type.</typeparam>
public sealed class DatabaseMigrator<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseMigratorOptions> options,
    ILogger<DatabaseMigrator<TContext>> logger)
    : IHostedService
    where TContext : AgentivaDbContext
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var contextName = typeof(TContext).Name;

        if (!options.Value.Enabled)
        {
            // Explicit, not silent: an operator reading the startup log for a
            // service that unexpectedly has an out-of-date schema should find
            // the reason here rather than have to know to look for an absence.
            logger.LogInformation(
                "{Context}: automatic migration is disabled (Migrator:Enabled=false). "
                + "The schema must be brought up to date by a separate migration step "
                + "before this service is expected to work correctly.",
                contextName);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        // The database container may still be starting during compose bring-up,
        // so wait for it rather than crash-looping the service.
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

            if (pending.Length == 0)
            {
                logger.LogInformation("{Context}: database schema is up to date.", contextName);
                return;
            }

            logger.LogInformation(
                "{Context}: applying {Count} pending migration(s): {Migrations}",
                contextName,
                pending.Length,
                string.Join(", ", pending));

            await context.Database.MigrateAsync(cancellationToken);

            logger.LogInformation("{Context}: migrations applied successfully.", contextName);
        });
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
