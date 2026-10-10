using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.Strategy.Domain.Entities;
using Agentiva.BuildingBlocks.TradingRules.Strategies;
using Agentiva.Strategy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Agentiva.Strategy.Infrastructure;

/// <summary>
/// Ensures every <see cref="IStrategy"/> the code knows about has a matching
/// <see cref="StrategyDefinition"/> row, and that row's recorded version
/// matches the code's.
/// </summary>
/// <remarks>
/// Without this, a fresh database would leave every strategy unattributable —
/// <c>MarketCandleCreatedHandler</c> looks a strategy up by name before it
/// will ever evaluate it, so "no definition row" means "never runs", not
/// "runs with a missing version". Runs after <c>DatabaseMigrator</c> for the
/// same reason <c>RiskPolicySeeder</c> does; see that type's remarks.
/// </remarks>
public sealed class StrategySeeder(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IStrategy> strategies,
    IClock clock,
    ILogger<StrategySeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<StrategyDbContext>();

        var executionStrategy = context.Database.CreateExecutionStrategy();

        await executionStrategy.ExecuteAsync(async () =>
        {
            var now = clock.UtcNow;

            foreach (var strategy in strategies)
            {
                var existing = await context.Strategies
                    .FirstOrDefaultAsync(s => s.Name == strategy.Name, cancellationToken);

                if (existing is null)
                {
                    var definition = StrategyDefinition.Create(
                        strategy.Name, DescriptionFor(strategy), strategy.Version, now);

                    context.Strategies.Add(definition);

                    logger.LogInformation(
                        "Seeded strategy {Name} version {Version}.", strategy.Name, strategy.Version);
                }
                else if (!string.Equals(existing.CurrentVersion, strategy.Version, StringComparison.Ordinal))
                {
                    var previousVersion = existing.CurrentVersion;
                    existing.UpdateVersion(strategy.Version, now);

                    logger.LogInformation(
                        "Strategy {Name} moved from recorded version {PreviousVersion} to {NewVersion}. "
                        + "Signals produced from now on are attributed to the new version.",
                        strategy.Name, previousVersion, strategy.Version);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        });
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string DescriptionFor(IStrategy strategy) => strategy switch
    {
        EmaRsiStrategy => "EMA(12/26) crossover filtered by RSI(14).",
        TrendFollowingStrategy => "Price and EMA50 aligned on the same side of EMA200.",
        BreakoutStrategy => "Close breaks the prior 20-bar Donchian channel.",
        _ => strategy.GetType().Name
    };
}
