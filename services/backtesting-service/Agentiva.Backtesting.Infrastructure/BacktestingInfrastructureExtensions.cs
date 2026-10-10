using Agentiva.Backtesting.Application.Abstractions;
using Agentiva.Backtesting.Application.Commands;
using Agentiva.Backtesting.Infrastructure.Persistence;
using Agentiva.Backtesting.Infrastructure.TimeSeries;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Agentiva.Backtesting.Infrastructure;

/// <summary>Composition root for the Backtesting Service.</summary>
public static class BacktestingInfrastructureExtensions
{
    /// <summary>Registers the backtesting application and infrastructure services.</summary>
    public static IServiceCollection AddBacktestingServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // --- Persistence: relational (backtesting_db) --------------------
        services.AddAgentivaDbContext<BacktestingDbContext>(configuration);
        services.AddAgentivaDatabaseMigrator<BacktestingDbContext>(configuration);

        services.AddScoped<IBacktestRunRepository, BacktestRunRepository>();

        // --- Historical data: time-series (shared market_db) -------------
        // Read-only — see the remarks on HistoricalCandleReader for why this
        // service reaches directly into the Market Data Service's table
        // rather than through its HTTP API.
        var timescaleConnectionString = configuration.GetConnectionString("Timescale")
            ?? throw new InvalidOperationException(
                "The 'Timescale' connection string is required by the Backtesting Service.");

        services.AddSingleton(NpgsqlDataSource.Create(timescaleConnectionString));
        services.AddScoped<IHistoricalCandleReader, HistoricalCandleReader>();

        // --- Application ---------------------------------------------------
        services.AddAgentivaMediator(typeof(RunBacktestCommand).Assembly);

        return services;
    }
}
