using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;
using Serilog.Formatting.Compact;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Agentiva.BuildingBlocks.Observability.Logging;

/// <summary>Configures Serilog for an Agentiva service.</summary>
public static class SerilogConfiguration
{
    /// <summary>
    /// Builds the logger configuration: compact JSON to stdout plus a direct
    /// push to Loki.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Logs are JSON on stdout, never human-formatted text. Container stdout is
    /// the one log channel that survives a crashing process, and structured
    /// output means a field such as <c>CorrelationId</c> stays queryable instead
    /// of being embedded in a sentence that has to be parsed with a regular
    /// expression.
    /// </para>
    /// <para>
    /// Noise from the framework is filtered at source rather than at query time:
    /// ASP.NET Core logs two Information events per request, which at market
    /// data volumes would dominate the log store and cost real money to retain.
    /// </para>
    /// </remarks>
    /// <param name="loggerConfiguration">The configuration to populate, in place.</param>
    /// <param name="serviceName">Logical service name, attached to every event.</param>
    /// <param name="configuration">Configuration root, read for the Serilog and Loki sections.</param>
    /// <param name="environment">Host environment.</param>
    public static LoggerConfiguration Apply(
        LoggerConfiguration loggerConfiguration,
        string serviceName,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var minimumLevel = configuration.GetValue("Serilog:MinimumLevel", LogEventLevel.Information);
        var lokiUrl = configuration.GetValue<string>("Loki:Url");

        loggerConfiguration
            .MinimumLevel.Is(minimumLevel)

            // Framework noise. Warning-and-above still surfaces genuine problems
            // such as a failed request pipeline or a dropped connection.
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("RabbitMQ.Client", LogEventLevel.Warning)

            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("service.name", serviceName)
            .Enrich.WithProperty("deployment.environment", environment.EnvironmentName)

            // Correlates a log line with its trace in Grafana.
            .Enrich.With<TraceContextEnricher>()

            .WriteTo.Console(new CompactJsonFormatter());

        if (!string.IsNullOrWhiteSpace(lokiUrl))
        {
            loggerConfiguration.WriteTo.GrafanaLoki(
                lokiUrl,
                labels:
                [
                    new LokiLabel { Key = "service", Value = serviceName },
                    new LokiLabel { Key = "environment", Value = environment.EnvironmentName }
                ],

                // Only service, environment and level become Loki stream labels
                // (the level is added by handleLogLevelAsLabel, on by default).
                // Nothing else is promoted: a high-cardinality label such as
                // correlation id would open a separate Loki stream per request
                // and wreck ingest performance, so those stay in the log body
                // where they are still fully searchable.
                propertiesAsLabels: [],

                // Bounded queue. If Loki is unreachable the sink drops events
                // once the queue fills rather than growing until the service
                // runs out of memory — losing log lines is survivable, a
                // trading service dying because its log sink is down is not.
                queueLimit: 50_000,
                period: TimeSpan.FromSeconds(2),
                restrictedToMinimumLevel: minimumLevel);
        }

        return loggerConfiguration;
    }
}
