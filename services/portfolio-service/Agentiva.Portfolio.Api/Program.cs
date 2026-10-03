using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;

// =============================================================================
// Agentiva — Portfolio Service
//
// Maintains balances, positions, average entry prices, realised and
// unrealised P&L, portfolio value and exposure from order and trade events.
//
// Phase 1 status: service skeleton. The host, configuration, observability,
// health probes, OpenAPI document and event-bus connection are production
// shaped and fully wired. The domain logic lands in Phase 6; see
// docs/architecture/phases.md.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "portfolio-service",
    displayName: "Portfolio Service",
    description: "Maintains balances, positions, average entry prices, realised and unrealised P&L, portfolio value and exposure from order and trade events.");

// Connects to the event bus as a publisher. Subscriptions are added in Phase 6,
// alongside the handlers that consume them — a queue bound to routing keys that
// nothing drains would silently accumulate messages.
builder.Services.AddAgentivaMessaging(builder.Configuration, "portfolio-service");

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();

await app.RunAsync();
