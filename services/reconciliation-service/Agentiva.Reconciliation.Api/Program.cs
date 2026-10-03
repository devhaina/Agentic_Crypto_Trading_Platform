using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Reconciliation Service
//
// Periodically compares internal state against the exchange. On any mismatch
// it disables trading and raises a critical alert rather than silently
// repairing financial state.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 6; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "reconciliation-service",
    displayName: "Reconciliation Service",
    description: "Periodically compares internal state against the exchange. On any mismatch it disables trading and raises a critical alert rather than silently repairing financial state.");

// Connects to the event bus as a publisher. Subscriptions are added in Phase 6,
// alongside the handlers that consume them — a queue bound to routing keys that
// nothing drains would silently accumulate messages.
builder.Services.AddAgentivaMessaging(builder.Configuration, "reconciliation-service");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
