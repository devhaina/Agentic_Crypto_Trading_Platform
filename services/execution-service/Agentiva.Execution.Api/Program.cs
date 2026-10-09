using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;
using Agentiva.Execution.Api.Endpoints;
using Agentiva.Execution.Infrastructure;

// =============================================================================
// Agentiva — Execution Service
//
// The only service permitted to contact an exchange. Owns exchange
// credentials, signs requests, enforces the trading mode at the execution
// boundary and guarantees order idempotency.
//
// Phase 5: a Binance REST client with request signing, an IExchangeExecution
// abstraction with a BinanceExecutionAdapter (Live) and a
// SimulatedExchangeExecution (Backtest/Paper), deterministic client-order-id
// idempotency, order lifecycle events, and the read API the Trading
// Service's real duplicate-order check calls.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "execution-service",
    displayName: "Execution Service",
    description: "The only service permitted to contact an exchange. Owns exchange credentials, signs requests, enforces the trading mode at the execution boundary and guarantees order idempotency.");

builder.Services.AddAgentivaMessaging(builder.Configuration, "execution-service");
builder.Services.AddExecutionServices(builder.Configuration);

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();
app.MapExecutionEndpoints();

await app.RunAsync();
