using Agentiva.Backtesting.Api.Endpoints;
using Agentiva.Backtesting.Infrastructure;
using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Backtesting Service
//
// Historical simulation of the Strategy Service's own deterministic rules
// (shared via Agentiva.BuildingBlocks.TradingRules, so a backtest replays
// the exact code that runs live) with explicit fee and slippage modelling,
// walk-forward validation and performance metrics. No exchange credential,
// no order placement: every run reads already-ingested candles and writes
// only to this service's own database.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "backtesting-service",
    displayName: "Backtesting Service",
    description: "Historical simulation with explicit fee and slippage modelling, walk-forward validation and performance metrics.");

builder.Services.AddBacktestingServices(builder.Configuration);

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();
app.MapBacktestEndpoints();

await app.RunAsync();
