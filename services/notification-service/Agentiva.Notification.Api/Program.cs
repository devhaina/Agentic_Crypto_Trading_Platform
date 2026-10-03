using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Notification Service
//
// Delivers operational alerts to configured channels and records delivery
// outcomes.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 11; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "notification-service",
    displayName: "Notification Service",
    description: "Delivers operational alerts to configured channels and records delivery outcomes.");

// Connects to the event bus as a publisher. Subscriptions are added in Phase 11,
// alongside the handlers that consume them — a queue bound to routing keys that
// nothing drains would silently accumulate messages.
builder.Services.AddAgentivaMessaging(builder.Configuration, "notification-service");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
