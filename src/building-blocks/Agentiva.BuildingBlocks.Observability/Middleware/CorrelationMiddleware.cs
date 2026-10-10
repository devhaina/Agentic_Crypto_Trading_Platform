using Agentiva.BuildingBlocks.Common.Correlation;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Agentiva.BuildingBlocks.Observability.Middleware;

/// <summary>
/// Establishes the correlation identifier for the request and propagates it to
/// logs, traces and the response.
/// </summary>
/// <remarks>
/// <para>
/// Accepts a caller-supplied <c>X-Correlation-Id</c> so that one user action in
/// the Angular dashboard can be followed through the gateway, the trading
/// service, the risk gate and the execution service as a single thread. When the
/// caller supplies none, one is minted here.
/// </para>
/// <para>
/// The supplied value is length-capped and filtered before use: it is attacker-
/// controlled input that ends up in log records, and an unbounded value would
/// let a caller inflate every log line for a request, or smuggle newlines into
/// a log sink to forge entries.
/// </para>
/// </remarks>
public sealed class CorrelationMiddleware(RequestDelegate next)
{
    private const int MaxCorrelationIdLength = 100;

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        var correlationId = Sanitize(context.Request.Headers[CorrelationHeaders.CorrelationId].FirstOrDefault())
                            ?? Guid.CreateVersion7().ToString();

        var requestId = context.TraceIdentifier;
        var agentRunId = Sanitize(context.Request.Headers[CorrelationHeaders.AgentRunId].FirstOrDefault());

        if (correlationContext is CorrelationContext mutable)
        {
            mutable.CorrelationId = correlationId;
            mutable.RequestId = requestId;
            mutable.AgentRunId = agentRunId;

            // Captured so this service's own downstream HTTP clients can
            // forward the caller's identity on a service-to-service call —
            // see the remarks on ICorrelationContext.AuthorizationHeader.
            // Deliberately never logged or tagged onto the trace, unlike
            // every other field captured here: this one is a bearer credential.
            mutable.AuthorizationHeader = context.Request.Headers.Authorization.FirstOrDefault();
        }

        // Echo it back so the browser's network tab and any intermediary can see
        // which correlation the response belongs to.
        context.Response.Headers[CorrelationHeaders.CorrelationId] = correlationId;

        // Tag the server span so the identifier is searchable in the trace store.
        var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("agentiva.correlation_id", correlationId);

        if (agentRunId is not null)
        {
            activity?.SetTag("agentiva.agent_run_id", agentRunId);
        }

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestId", requestId))
        using (LogContext.PushProperty("AgentRunId", agentRunId))
        {
            await next(context);
        }
    }

    /// <summary>
    /// Returns the value when it is a safe correlation identifier, otherwise null.
    /// </summary>
    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCorrelationIdLength)
        {
            return null;
        }

        // Allow only characters that appear in GUIDs and similar trace ids.
        // Rejecting control characters is what prevents log forging via an
        // injected newline.
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':'))
            {
                return null;
            }
        }

        return value;
    }
}
