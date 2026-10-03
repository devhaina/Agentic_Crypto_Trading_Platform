using Agentiva.BuildingBlocks.Observability.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Agentiva.BuildingBlocks.Observability.Tracing;

/// <summary>Configures OpenTelemetry traces and metrics for an Agentiva service.</summary>
public static class OpenTelemetryConfiguration
{
    /// <summary>
    /// Registers tracing and metrics, exporting both over OTLP to the collector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything leaves the service over OTLP to an OpenTelemetry Collector,
    /// which Prometheus then scrapes. The alternative — having each service host
    /// its own <c>/metrics</c> endpoint — would need
    /// <c>OpenTelemetry.Exporter.Prometheus.AspNetCore</c>, which has only ever
    /// shipped as a beta package and is not something to put on the critical
    /// path of a financial system. Routing through the collector also means
    /// sampling, batching and redaction are configured in one place instead of
    /// in fourteen services.
    /// </para>
    /// <para>
    /// Trace sampling is configured via the standard <c>OTEL_TRACES_SAMPLER</c>
    /// environment variables, so it can be dialled down in production without a
    /// code change. Local development samples everything.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">Logical service name, used as the OTel resource name.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <param name="environment">Host environment.</param>
    /// <param name="extraMeters">Additional meter names this service exposes.</param>
    /// <param name="extraActivitySources">Additional activity source names this service exposes.</param>
    public static IServiceCollection AddAgentivaOpenTelemetry(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration,
        IHostEnvironment environment,
        string[]? extraMeters = null,
        string[]? extraActivitySources = null)
    {
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                           ?? configuration["OpenTelemetry:Endpoint"];

        // Registered here so every service gets the business metrics without
        // its own registration call.
        services.AddSingleton<AgentivaMetrics>();
        services.AddMetrics();

        var builder = services.AddOpenTelemetry();

        builder.ConfigureResource(resource => resource
            .AddService(
                serviceName: serviceName,
                serviceVersion: typeof(OpenTelemetryConfiguration).Assembly.GetName().Version?.ToString() ?? "0.1.0",
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment", environment.EnvironmentName)
            ]));

        builder.WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;

                    // Health and metrics probes fire constantly and carry no
                    // diagnostic value; tracing them would bury real requests.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health")
                        && !context.Request.Path.StartsWithSegments("/alive")
                        && !context.Request.Path.StartsWithSegments("/ready")
                        && !context.Request.Path.StartsWithSegments("/metrics");
                })
                .AddHttpClientInstrumentation(options => options.RecordException = true)

                // Database spans, from the stable Npgsql tracing package rather
                // than the beta EF Core instrumentation.
                .AddNpgsql()

                .AddSource("Agentiva.Messaging.Publisher")
                .AddSource("Agentiva.Messaging.Consumer")
                .AddSource("Agentiva.Trading")
                .AddSource("Agentiva.Risk")
                .AddSource("Agentiva.Execution");

            if (extraActivitySources is not null)
            {
                foreach (var source in extraActivitySources)
                {
                    tracing.AddSource(source);
                }
            }

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
            }
        });

        builder.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(AgentivaMetrics.MeterName)
                .AddMeter("Microsoft.AspNetCore.Hosting")
                .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                .AddNpgsqlInstrumentation();

            if (extraMeters is not null)
            {
                metrics.AddMeter(extraMeters);
            }

            // Explicit latency buckets in milliseconds. The SDK default buckets
            // top out too low to distinguish a healthy exchange round trip from
            // a pathological one, and execution latency is the metric an
            // operator reaches for first when fills look wrong.
            metrics.AddView(
                instrumentName: "agentiva_execution_latency_ms",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [5, 10, 25, 50, 100, 250, 500, 1_000, 2_500, 5_000, 10_000]
                });

            metrics.AddView(
                instrumentName: "agentiva_market_data_latency_ms",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [1, 5, 10, 25, 50, 100, 250, 500, 1_000, 5_000]
                });

            metrics.AddView(
                instrumentName: "agentiva_agent_duration_ms",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [100, 500, 1_000, 2_500, 5_000, 10_000, 30_000, 60_000, 120_000]
                });

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
            }
        });

        return services;
    }
}
