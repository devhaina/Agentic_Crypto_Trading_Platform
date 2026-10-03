using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Backtesting Service
//
// Historical simulation with explicit fee and slippage modelling, walk-
// forward validation and performance metrics.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 8; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "backtesting-service",
    displayName: "Backtesting Service",
    description: "Historical simulation with explicit fee and slippage modelling, walk-forward validation and performance metrics.");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
