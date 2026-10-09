using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.Persistence;
using Agentiva.Contracts.Events;
using Agentiva.Contracts.Events.Market;
using Agentiva.Strategy.Application.Abstractions;
using Agentiva.Strategy.Application.Queries;
using Agentiva.Strategy.Domain.Strategies;
using Agentiva.Strategy.Infrastructure.Caching;
using Agentiva.Strategy.Infrastructure.Candles;
using Agentiva.Strategy.Infrastructure.Configuration;
using Agentiva.Strategy.Infrastructure.EventHandling;
using Agentiva.Strategy.Infrastructure.Persistence;
using Agentiva.Strategy.Infrastructure.TimeSeries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Agentiva.Strategy.Infrastructure;

/// <summary>Composition root for the Strategy Service.</summary>
public static class StrategyInfrastructureExtensions
{
    /// <summary>Registers the strategy application and infrastructure services.</summary>
    public static IServiceCollection AddStrategyServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Configuration -------------------------------------------------
        services
            .AddOptions<StrategyOptions>()
            .Bind(configuration.GetSection(StrategyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- Persistence: relational (strategy_db) ------------------------
        services.AddAgentivaDbContext<StrategyDbContext>(configuration);
        services.AddAgentivaOutboxProcessor<StrategyDbContext>(configuration);
        services.AddAgentivaDatabaseMigrator<StrategyDbContext>(configuration);

        services.AddScoped<IStrategyDefinitionRepository, StrategyDefinitionRepository>();
        services.AddScoped<ISignalRepository, SignalRepository>();

        // --- Persistence: time-series (shared market_db) -------------------
        // Raw Npgsql against the Market Data Service's TimescaleDB hypertable
        // — see the remarks on IndicatorSnapshotWriter for why this service
        // writes there directly instead of through an HTTP call.
        var timescaleConnectionString = configuration.GetConnectionString("Timescale")
            ?? throw new InvalidOperationException(
                "The 'Timescale' connection string is required by the Strategy Service.");

        services.AddSingleton(NpgsqlDataSource.Create(timescaleConnectionString));
        services.AddSingleton<IndicatorSnapshotWriter>();

        // --- Caching -------------------------------------------------------------
        // IConnectionMultiplexer is already registered as a singleton by
        // AddAgentivaServiceDefaults when ConnectionStrings:Redis is configured.
        services.AddSingleton<VolatilityCache>();

        // --- In-memory candle window ----------------------------------------
        services.AddSingleton(sp => new CandleBufferStore(
            sp.GetRequiredService<IOptions<StrategyOptions>>().Value.CandleBufferCapacity));

        // --- The strategies themselves ---------------------------------------
        services.AddSingleton<IStrategy, EmaRsiStrategy>();
        services.AddSingleton<IStrategy, TrendFollowingStrategy>();
        services.AddSingleton<IStrategy, BreakoutStrategy>();

        // --- Application -------------------------------------------------------
        services.AddAgentivaMediator(typeof(GetRecentSignalsQuery).Assembly);

        // --- Seeding and ingestion, in dependency order --------------------------
        // Seeding needs the migrated schema; the consumer needs the seeded
        // strategy rows to attribute a signal to. ServiceDefaults starts
        // hosted services sequentially in registration order for exactly
        // this reason — see docs/operations/migrations.md.
        services.AddHostedService<StrategySeeder>();

        services.Subscribe<MarketCandleCreated, MarketCandleCreatedHandler>(EventTypes.Market.CandleCreated);
        services.AddAgentivaEventConsumer();

        return services;
    }
}
