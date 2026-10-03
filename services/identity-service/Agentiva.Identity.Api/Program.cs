using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Identity Service
//
// Authentication, authorisation and user/role management. Issues the JWTs
// every other service validates.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 11; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "identity-service",
    displayName: "Identity Service",
    description: "Authentication, authorisation and user/role management. Issues the JWTs every other service validates.");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
