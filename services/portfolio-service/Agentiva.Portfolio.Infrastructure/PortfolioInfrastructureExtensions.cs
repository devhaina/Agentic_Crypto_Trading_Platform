using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.Persistence;
using Agentiva.Contracts.Events;
using Agentiva.Contracts.Events.Orders;
using Agentiva.Portfolio.Application.Abstractions;
using Agentiva.Portfolio.Application.Configuration;
using Agentiva.Portfolio.Application.Positions;
using Agentiva.Portfolio.Infrastructure.EventHandling;
using Agentiva.Portfolio.Infrastructure.Persistence;
using Agentiva.Portfolio.Infrastructure.Providers;
using Agentiva.Portfolio.Infrastructure.TimeSeries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Agentiva.Portfolio.Infrastructure;

/// <summary>Composition root for the Portfolio Service.</summary>
public static class PortfolioInfrastructureExtensions
{
    /// <summary>Registers the portfolio application and infrastructure services.</summary>
    public static IServiceCollection AddPortfolioServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Configuration -------------------------------------------------
        services
            .AddOptions<PortfolioOptions>()
            .Bind(configuration.GetSection(PortfolioOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- Persistence: relational (portfolio_db) -------------------------
        services.AddAgentivaDbContext<PortfolioDbContext>(configuration);
        services.AddAgentivaOutboxProcessor<PortfolioDbContext>(configuration);
        services.AddAgentivaDatabaseMigrator<PortfolioDbContext>(configuration);

        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPortfolioAccountRepository, PortfolioAccountRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();

        // --- Persistence: time-series (shared market_db) -------------------
        // Raw Npgsql against the Strategy Service's own hypertable schema —
        // see the remarks on PortfolioSnapshotWriter for why this service
        // writes there directly instead of through an HTTP call.
        var timescaleConnectionString = configuration.GetConnectionString("Timescale")
            ?? throw new InvalidOperationException(
                "The 'Timescale' connection string is required by the Portfolio Service.");

        services.AddSingleton(NpgsqlDataSource.Create(timescaleConnectionString));
        services.AddSingleton<PortfolioSnapshotWriter>();

        // --- Live price lookups ----------------------------------------------
        // IConnectionMultiplexer is already registered as a singleton by
        // AddAgentivaServiceDefaults when ConnectionStrings:Redis is configured.
        services.AddScoped<IMarkPriceProvider, RedisMarkPriceProvider>();

        // --- Application -------------------------------------------------------
        services.AddAgentivaMediator(typeof(GetPositionsQuery).Assembly);

        // --- Event consumption ---------------------------------------------------
        services.Subscribe<OrderFilled, OrderFilledHandler>(EventTypes.Orders.Filled);
        services.Subscribe<OrderPartiallyFilled, OrderPartiallyFilledHandler>(EventTypes.Orders.PartiallyFilled);
        services.AddAgentivaEventConsumer();

        return services;
    }
}
