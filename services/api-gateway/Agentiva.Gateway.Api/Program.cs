using System.Threading.RateLimiting;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.ServiceDefaults;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Transforms;

// =============================================================================
// Agentiva — API Gateway
//
// The single ingress for the Angular dashboard and any external client. It
// authenticates the caller, applies rate limits and CORS, stamps a correlation
// identifier, and routes to the owning service. No business logic lives here:
// a gateway that makes decisions becomes a distributed monolith's bottleneck
// and a place where authorisation rules quietly diverge from the services that
// enforce them.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentivaServiceDefaults(
    serviceName: "api-gateway",
    displayName: "API Gateway",
    description: "Single ingress: authentication, authorisation, rate limiting, CORS, "
                 + "correlation and routing to the owning service.");

// --- Reverse proxy ----------------------------------------------------------
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
    {
        // Propagate the correlation identifier downstream. The gateway has
        // already minted one by this point (CorrelationMiddleware runs first),
        // so every service in the chain shares a single identifier rather than
        // each generating its own.
        context.AddRequestTransform(async transformContext =>
        {
            var correlation = transformContext.HttpContext.RequestServices
                .GetRequiredService<ICorrelationContext>();

            transformContext.ProxyRequest.Headers.Remove(CorrelationHeaders.CorrelationId);
            transformContext.ProxyRequest.Headers.Add(
                CorrelationHeaders.CorrelationId, correlation.CorrelationId);

            await ValueTask.CompletedTask;
        });
    });

// --- CORS -------------------------------------------------------------------
const string DashboardCorsPolicy = "agentiva-dashboard";

builder.Services.AddCors(options =>
{
    // Explicit origin allow-list, read from configuration. Never a wildcard:
    // the dashboard sends credentials, and AllowAnyOrigin with credentials is
    // both rejected by browsers and an open invitation to CSRF.
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? ["http://localhost:4200"];

    options.AddPolicy(DashboardCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .WithExposedHeaders(CorrelationHeaders.CorrelationId));
});

// --- Rate limiting ----------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partitioned per authenticated user, falling back to remote IP for
    // anonymous callers. A single global limiter would let one noisy client
    // exhaust the budget for everyone.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst("sub")?.Value ?? "authenticated"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
        {
            // A token bucket rather than a fixed window: it absorbs the burst a
            // dashboard produces on page load while still bounding sustained rate.
            TokenLimit = 200,
            TokensPerPeriod = 100,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });

    // Order submission gets its own, much tighter budget. Rate limiting is a
    // genuine safety control here, not just capacity management: a runaway
    // client or a misbehaving agent loop must not be able to fire hundreds of
    // trading intents per second.
    options.AddTokenBucketLimiter("trading-writes", limiter =>
    {
        limiter.TokenLimit = 20;
        limiter.TokensPerPeriod = 10;
        limiter.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});

var app = builder.Build();

app.UseAgentivaServiceDefaults();

app.UseCors(DashboardCorsPolicy);
app.UseRateLimiter();

app.MapAgentivaServiceInfo();

// All proxied routes require an authenticated caller unless the route's own
// configuration opts out (the identity service's login endpoints do).
app.MapReverseProxy();

await app.RunAsync();
