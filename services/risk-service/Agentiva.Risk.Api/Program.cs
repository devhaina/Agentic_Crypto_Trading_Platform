using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;
using Agentiva.Risk.Api.Endpoints;
using Agentiva.Risk.Infrastructure;

// =============================================================================
// Agentiva — Risk Service
//
// The deterministic hard gate. Every trading intent, however it originated —
// an AI proposal, a strategy signal, a manual request — must pass through here
// before an order can exist, and the decision is made by pure code with no
// model call and no randomness.
//
// This service also owns position sizing. No caller can influence the size
// beyond supplying an entry and a stop; the sizer does not read any size hint,
// which is the structural reason an LLM cannot size a position.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "risk-service",
    displayName: "Risk Service",
    description: "Deterministic risk gate and position sizing. Approves or rejects every "
                 + "trading intent against configured limits, and derives position size from "
                 + "a bounded risk budget with fees, slippage and exchange precision included.");

builder.Services.AddAgentivaMessaging(builder.Configuration, "risk-service");
builder.Services.AddRiskServices(builder.Configuration);

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();
app.MapRiskEndpoints();

await app.RunAsync();
