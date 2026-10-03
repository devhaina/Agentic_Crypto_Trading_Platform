using System.Text.Json;
using Agentiva.BuildingBlocks.Common.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Agentiva.BuildingBlocks.Observability.Health;

/// <summary>Health check tags used to separate liveness from readiness.</summary>
public static class HealthTags
{
    /// <summary>
    /// The process is alive. Checks tagged this way must not touch a dependency.
    /// </summary>
    public const string Live = "live";

    /// <summary>
    /// The service can serve traffic, including its required dependencies.
    /// </summary>
    public const string Ready = "ready";
}

/// <summary>Maps the platform's health endpoints.</summary>
public static class HealthEndpoints
{
    /// <summary>
    /// Maps <c>/alive</c>, <c>/ready</c> and <c>/health</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The split between liveness and readiness matters in Kubernetes and it is
    /// routinely got wrong. A liveness probe that checks the database will fail
    /// during a brief database outage, and the kubelet will respond by killing
    /// and restarting every replica of every service — turning a recoverable
    /// dependency blip into a full outage, with a thundering herd of
    /// reconnecting pods on top.
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>/alive</c> — process-local only. Fails only if the process is
    ///     genuinely wedged, which is the one case where restarting helps.
    ///   </description></item>
    ///   <item><description>
    ///     <c>/ready</c> — includes dependencies. Failing removes the pod from
    ///     the load balancer without killing it, so it can recover in place.
    ///   </description></item>
    ///   <item><description>
    ///     <c>/health</c> — everything, with per-check detail, for humans and
    ///     for the dashboard.
    ///   </description></item>
    /// </list>
    /// </remarks>
    public static IEndpointRouteBuilder MapAgentivaHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Live),
            ResponseWriter = WriteBriefResponseAsync
        });

        endpoints.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Ready),
            ResponseWriter = WriteBriefResponseAsync
        });

        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteDetailedResponseAsync
        });

        return endpoints;
    }

    private static Task WriteBriefResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(new { status = report.Status.ToString() }, AgentivaJson.Options));
    }

    private static Task WriteDetailedResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new HealthResponse(
            report.Status.ToString(),
            (int)report.TotalDuration.TotalMilliseconds,
            report.Entries.Select(entry => new HealthEntryResponse(
                entry.Key,
                entry.Value.Status.ToString(),
                (int)entry.Value.Duration.TotalMilliseconds,

                // The description is safe to surface. The exception is not:
                // a connection failure message routinely contains the host,
                // port and user name of the dependency, and /health is often
                // reachable more widely than intended. The full exception is
                // logged instead.
                entry.Value.Description,
                entry.Value.Tags.ToArray()))
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ToArray());

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, AgentivaJson.Options));
    }
}

/// <summary>Aggregate health of a service.</summary>
/// <param name="Status">Overall status.</param>
/// <param name="TotalDurationMs">How long the whole report took.</param>
/// <param name="Checks">Individual check results.</param>
public sealed record HealthResponse(string Status, int TotalDurationMs, HealthEntryResponse[] Checks);

/// <summary>Result of a single health check.</summary>
/// <param name="Name">Check name.</param>
/// <param name="Status">Check status.</param>
/// <param name="DurationMs">How long the check took.</param>
/// <param name="Description">Operator-facing description, free of connection detail.</param>
/// <param name="Tags">Tags, which determine the endpoints the check appears on.</param>
public sealed record HealthEntryResponse(
    string Name,
    string Status,
    int DurationMs,
    string? Description,
    string[] Tags);
