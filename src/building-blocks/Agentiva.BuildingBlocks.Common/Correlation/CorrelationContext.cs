namespace Agentiva.BuildingBlocks.Common.Correlation;

/// <summary>
/// Well-known header and log-property names that stitch a single business
/// operation together across Angular, the gateway, every .NET service, the
/// Python agent platform and RabbitMQ.
/// </summary>
public static class CorrelationHeaders
{
    /// <summary>Identifies one end-to-end business operation (one user action).</summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>Identifies one inbound request within a correlation.</summary>
    public const string RequestId = "X-Request-Id";

    /// <summary>
    /// Caller-supplied key that makes a financial command idempotent.
    /// Required on every state-changing trading endpoint.
    /// </summary>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>Identifies the originating AI agent run, when a trade traces back to one.</summary>
    public const string AgentRunId = "X-Agent-Run-Id";
}

/// <summary>Ambient correlation data for the current request or message.</summary>
public interface ICorrelationContext
{
    /// <summary>The end-to-end correlation identifier. Never empty.</summary>
    string CorrelationId { get; }

    /// <summary>Identifier of the current inbound request or message.</summary>
    string RequestId { get; }

    /// <summary>Originating AI agent run, when applicable.</summary>
    string? AgentRunId { get; }
}

/// <summary>Mutable <see cref="ICorrelationContext"/> populated by middleware or a message consumer.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public string? AgentRunId { get; set; }
}
