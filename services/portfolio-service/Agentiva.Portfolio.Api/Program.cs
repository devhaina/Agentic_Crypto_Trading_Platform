using Agentiva.BuildingBlocks.Messaging.RabbitMq;
using Agentiva.BuildingBlocks.ServiceDefaults;
using Agentiva.Portfolio.Api.Endpoints;
using Agentiva.Portfolio.Infrastructure;

// =============================================================================
// Agentiva — Portfolio Service
//
// Maintains balances, positions, average entry prices, realised and
// unrealised P&L, portfolio value and exposure from order fill events.
//
// Phase 6: consumes order.filled/order.partiallyFilled, builds positions
// and a cash balance from them with average-cost accounting, records closed
// round trips, and serves the Trading Service's real portfolio snapshot and
// the dashboard's real Positions page.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "portfolio-service",
    displayName: "Portfolio Service",
    description: "Maintains balances, positions, average entry prices, realised and unrealised P&L, portfolio value and exposure from order and trade events.");

builder.Services.AddAgentivaMessaging(builder.Configuration, "portfolio-service");
builder.Services.AddPortfolioServices(builder.Configuration);

var app = builder.Build();

app.UseAgentivaServiceDefaults();
app.MapAgentivaServiceInfo();
app.MapPortfolioEndpoints();

await app.RunAsync();
