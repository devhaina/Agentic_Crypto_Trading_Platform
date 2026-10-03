using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Strategy Service
//
// Deterministic indicator and strategy engine. Produces versioned trading
// signals. Never executes orders.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 3; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "strategy-service",
    displayName: "Strategy Service",
    description: "Deterministic indicator and strategy engine. Produces versioned trading signals. Never executes orders.");

// Connects to the event bus as a publisher. Subscriptions are added in Phase 3,
// alongside the handlers that consume them — a queue bound to routing keys that
// nothing drains would silently accumulate messages.
builder.Services.AddAgentivaMessaging(builder.Configuration, "strategy-service");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
