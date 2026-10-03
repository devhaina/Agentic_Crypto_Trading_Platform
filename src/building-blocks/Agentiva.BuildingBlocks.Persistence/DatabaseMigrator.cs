using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Agentiva.BuildingBlocks.Persistence;

/// <summary>Applies pending migrations once at startup.</summary>
/// <typeparam name="TContext">The service's context type.</typeparam>
public sealed class DatabaseMigrator<TContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseMigrator<TContext>> logger)
    : IHostedService
    where TContext : AgentivaDbContext
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        var contextName = typeof(TContext).Name;

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
