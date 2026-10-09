using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;
using Agentiva.MarketData.Api.Endpoints;
using Agentiva.MarketData.Infrastructure;

// =============================================================================
// Agentiva — Market Data Service
//
// Connects to Binance's public WebSocket market streams, normalises every
// message into a domain entity, writes it to the TimescaleDB hypertables
// provisioned in Phase 1, caches the latest value per symbol in Redis and
// publishes the market.* domain events every other service's market
// condition checks ultimately depend on.
//
// No exchange credentials live here: every stream this service subscribes to
// is public market data. See docs/architecture/known-limitations.md and the
// AI trust boundary notes for why that separation is load-bearing.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "market-data-service",
    displayName: "Market Data Service",
    description: "Ingests Binance market data over WebSocket, normalises it, stores time-series in TimescaleDB, caches the latest values in Redis and publishes market domain events.");

builder.Services.AddAgentivaMessaging(builder.Configuration, "market-data-service");
builder.Services.AddMarketDataServices(builder.Configuration);

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();
app.MapMarketDataEndpoints();

await app.RunAsync();
