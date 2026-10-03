using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Agentiva.BuildingBlocks.Observability.Logging;

/// <summary>
/// Attaches the ambient trace and span identifiers to every log event.
/// </summary>
/// <remarks>
/// This is what makes "jump from this log line to its distributed trace" work in
/// Grafana. Without <c>TraceId</c> on the log event, correlating a Loki line
/// with a Tempo or Jaeger span means guessing from timestamps.
/// </remarks>
public sealed class TraceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;

        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
