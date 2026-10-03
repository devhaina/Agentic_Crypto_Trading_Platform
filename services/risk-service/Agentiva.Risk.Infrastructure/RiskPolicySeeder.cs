using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.Application.Configuration;
using Agentiva.Risk.Domain.Policies;
using Agentiva.Risk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.Risk.Infrastructure;

/// <summary>
/// Ensures a default risk policy exists.
/// </summary>
/// <remarks>
/// <para>
/// Without a default policy the risk gate refuses every evaluation — correctly,
/// since "no policy" must never mean "no limits" — so a fresh database would
/// leave the platform unable to trade at all. This seeds the conservative
/// default on first start.
/// </para>
/// <para>
/// Runs after <c>DatabaseMigrator</c>, which is why hosted services are
/// configured to start sequentially rather than concurrently in
/// <c>ServiceDefaults</c>: seeding a table that does not exist yet would fail.
/// </para>
/// </remarks>
public sealed class RiskPolicySeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingOptions> tradingOptions,
    IClock clock,
    ILogger<RiskPolicySeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RiskDbContext>();

        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            if (await context.RiskPolicies.AnyAsync(p => p.IsDefault, cancellationToken))
            {
                logger.LogInformation("A default risk policy already exists; seeding skipped.");
                return;
            }

            var quoteAsset = AssetCode.Create(tradingOptions.Value.QuoteAsset);
            var policy = RiskPolicy.CreateDefault(clock.UtcNow, quoteAsset);

            context.RiskPolicies.Add(policy);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Seeded the default risk policy '{PolicyName}': {MaxRisk} per trade, {MaxDailyLoss} "
                + "per day, {MaxExposure} maximum exposure, {MaxPositions} open positions, "
                + "{MaxNotional} per position.",
                policy.Name,
                policy.MaxRiskPerTrade,
                policy.MaxDailyLoss,
                policy.MaxPortfolioExposure,
                policy.MaxOpenPositions,
                policy.MaxPositionNotional);
        });
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
