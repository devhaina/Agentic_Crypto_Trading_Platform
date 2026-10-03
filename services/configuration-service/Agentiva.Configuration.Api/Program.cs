using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Configuration Service
//
// Centralised, versioned and audited runtime configuration for the platform.
// Risk policy storage is deliberately kept separate, in the Risk Service.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 11; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "configuration-service",
    displayName: "Configuration Service",
    description: "Centralised, versioned and audited runtime configuration for the platform. Risk policy storage is deliberately kept separate, in the Risk Service.");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
