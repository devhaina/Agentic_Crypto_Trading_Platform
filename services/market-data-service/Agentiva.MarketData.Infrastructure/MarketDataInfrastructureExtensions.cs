using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Queries;
using Agentiva.MarketData.Infrastructure.Caching;
using Agentiva.MarketData.Infrastructure.Configuration;
using Agentiva.MarketData.Infrastructure.Ingestion;
using Agentiva.MarketData.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;

namespace Agentiva.MarketData.Infrastructure;

/// <summary>Composition root for the Market Data Service.</summary>
public static class MarketDataInfrastructureExtensions
{
    /// <summary>Registers the market data application and infrastructure services.</summary>
    public static IServiceCollection AddMarketDataServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Configuration -------------------------------------------------
        services
            .AddOptions<MarketDataOptions>()
            .Bind(configuration.GetSection(MarketDataOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<BinanceOptions>()
            .Bind(configuration.GetSection(BinanceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- Persistence -----------------------------------------------------
        // A single pooled data source for the process, per the modern Npgsql
        // pattern — not an EF Core context, because EF cannot express the
        // hypertables this schema was provisioned with. See
        // infrastructure/timescale/init/01-market-schema.sql.
        var timescaleConnectionString = configuration.GetConnectionString("Timescale")
            ?? throw new InvalidOperationException(
                "The 'Timescale' connection string is required by the Market Data Service.");

        services.AddSingleton(NpgsqlDataSource.Create(timescaleConnectionString));
        services.AddSingleton<TimescaleMarketDataRepository>();
        services.AddSingleton<IMarketDataReadRepository>(sp => sp.GetRequiredService<TimescaleMarketDataRepository>());

        // --- Caching -----------------------------------------------------------
        // IConnectionMultiplexer is already registered as a singleton by
        // AddAgentivaServiceDefaults when ConnectionStrings:Redis is configured.
        services.AddSingleton<IMarketDataCache, RedisMarketDataCache>();

        // --- Live feed state ----------------------------------------------------
        services.AddSingleton<MarketFeedState>();
        services.AddSingleton<IMarketFeedStatusProvider>(sp => sp.GetRequiredService<MarketFeedState>());

        // --- Application ---------------------------------------------------------
        services.AddAgentivaMediator(typeof(GetTickerQuery).Assembly);

        // --- Ingestion ----------------------------------------------------------
        services.AddHostedService<MarketDataIngestionWorker>();
        services.AddHostedService<StalenessMonitorWorker>();

        return services;
    }
}
