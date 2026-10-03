using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Market Data Service
//
// Ingests Binance market data over WebSocket, normalises it, stores time-
// series in TimescaleDB, caches the latest values in Redis and publishes
// market domain events.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 2; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "market-data-service",
    displayName: "Market Data Service",
    description: "Ingests Binance market data over WebSocket, normalises it, stores time-series in TimescaleDB, caches the latest values in Redis and publishes market domain events.");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
