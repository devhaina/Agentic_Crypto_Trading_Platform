using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Execution Service
//
// The only service permitted to contact an exchange. Owns exchange
// credentials, signs requests, enforces the trading mode at the execution
// boundary and guarantees order idempotency.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 5; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "execution-service",
    displayName: "Execution Service",
    description: "The only service permitted to contact an exchange. Owns exchange credentials, signs requests, enforces the trading mode at the execution boundary and guarantees order idempotency.");

// Connects to the event bus as a publisher. Subscriptions are added in Phase 5,
// alongside the handlers that consume them — a queue bound to routing keys that
// nothing drains would silently accumulate messages.
builder.Services.AddAgentivaMessaging(builder.Configuration, "execution-service");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
