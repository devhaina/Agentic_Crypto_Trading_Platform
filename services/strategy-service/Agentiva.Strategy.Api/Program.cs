using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;
using Agentiva.Strategy.Api.Endpoints;
using Agentiva.Strategy.Infrastructure;

// =============================================================================
// Agentiva — Strategy Service
//
// Consumes market.candle.created, computes EMA/RSI/MACD/ATR/VWAP over an
// in-memory rolling window per symbol and timeframe, evaluates every active
// deterministic strategy against them, and records and publishes whatever
// buy or sell signals come out of it. A signal is a recommendation, not an
// instruction: it still has to pass the Trading Service workflow and the
// deterministic risk gate before any order exists, and this service never
// contacts the Execution Service.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "strategy-service",
    displayName: "Strategy Service",
    description: "Deterministic indicator and strategy engine. Produces versioned trading signals. Never executes orders.");

builder.Services.AddAgentivaMessaging(builder.Configuration, "strategy-service");
builder.Services.AddStrategyServices(builder.Configuration);

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();
app.MapStrategyEndpoints();

await app.RunAsync();
