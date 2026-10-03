using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Audit Service
//
// Append-only record of every financial decision, enabling full
// reconstruction of why a trade was proposed, approved, executed and what it
// earned.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 5; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "audit-service",
    displayName: "Audit Service",
    description: "Append-only record of every financial decision, enabling full reconstruction of why a trade was proposed, approved, executed and what it earned.");

// Connects to the event bus as a publisher. Subscriptions are added in Phase 5,
// alongside the handlers that consume them — a queue bound to routing keys that
// nothing drains would silently accumulate messages.
builder.Services.AddAgentivaMessaging(builder.Configuration, "audit-service");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
