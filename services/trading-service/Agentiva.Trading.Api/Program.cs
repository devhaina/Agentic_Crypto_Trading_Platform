using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;
using Agentiva.Trading.Api.Endpoints;
using Agentiva.Trading.Infrastructure;

// =============================================================================
// Agentiva — Trading Service
//
// Owns the trade intent and the workflow around it:
//
//     intent recorded -> risk gate -> approved -> execution
//
// Every origin — a strategy signal, an AI proposal, a manual request — enters
// through the same command, so there is no path that reaches execution without
// a recorded risk decision. The intent is persisted before the risk gate is
// called, so a crash mid-workflow leaves a visible, non-executable record
// rather than losing the attempt entirely.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "trading-service",
    displayName: "Trading Service",
    description: "Owns trading intents and the intent-to-execution workflow. Records every "
                 + "intent, submits it to the deterministic risk gate, and never advances an "
                 + "unapproved intent to execution.");

builder.Services.AddAgentivaMessaging(builder.Configuration, "trading-service");
builder.Services.AddTradingServices(builder.Configuration);

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();
app.MapTradingEndpoints();

await app.RunAsync();
