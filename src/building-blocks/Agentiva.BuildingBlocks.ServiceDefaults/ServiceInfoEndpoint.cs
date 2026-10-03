using Agentiva.BuildingBlocks.Application.Configuration;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.ServiceDefaults.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Agentiva.BuildingBlocks.ServiceDefaults;

/// <summary>Maps the service self-description endpoint.</summary>
public static class ServiceInfoEndpoint
{
    /// <summary>
    /// Maps <c>GET /api/v1/info</c>, describing this service and its effective
    /// trading mode.
    /// </summary>
    /// <remarks>
    /// Anonymous by design, and deliberately limited to non-sensitive facts: a
    /// name, a version, the trading mode and the kill-switch state. The Angular
    /// dashboard's platform-status page polls every service's endpoint to build
    /// its service grid, and the gateway uses it for a readiness overview. No
    /// connection details, dependency hosts or configuration values are exposed.
    /// </remarks>
    public static IEndpointRouteBuilder MapAgentivaServiceInfo(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/info", (
                ServiceInfo serviceInfo,
                IOptions<TradingOptions> trading,
                IHostEnvironment environment,
                IClock clock) =>
            {
                var options = trading.Value;

                return Results.Ok(new ServiceInfoResponse(
                    serviceInfo.Name,
                    serviceInfo.DisplayName,
                    serviceInfo.Version,
                    serviceInfo.Description,
                    environment.EnvironmentName,
                    options.EffectiveMode.ToString().ToUpperInvariant(),
                    options.IsLiveRequestedButBlocked,
                    options.KillSwitchEnabled,
                    clock.UtcNow));
            })
            .WithName("GetServiceInfo")
            .WithTags("Platform")
            .WithSummary("Describes this service and its effective trading mode.")
            .AllowAnonymous();

        return endpoints;
    }
}

/// <summary>Self-description of a running service.</summary>
/// <param name="Name">Logical service name.</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="Version">Assembly version.</param>
/// <param name="Description">What the service does.</param>
/// <param name="Environment">Host environment name.</param>
/// <param name="TradingMode">Effective trading mode after the live-trading guard.</param>
/// <param name="LiveTradingBlocked">
/// True when configuration requested live trading and the guard downgraded it to paper.
/// </param>
/// <param name="KillSwitchEnabled">Whether the global kill switch is engaged.</param>
/// <param name="ServerTimeUtc">Server time, so a client can detect clock skew.</param>
public sealed record ServiceInfoResponse(
    string Name,
    string DisplayName,
    string Version,
    string Description,
    string Environment,
    string TradingMode,
    bool LiveTradingBlocked,
    bool KillSwitchEnabled,
    DateTimeOffset ServerTimeUtc);
