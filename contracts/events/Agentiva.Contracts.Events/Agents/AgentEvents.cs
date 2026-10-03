namespace Agentiva.Contracts.Events.Agents;

/// <summary>
/// One AI agent finished an analysis run.
/// </summary>
/// <remarks>
/// Purely observational: this event records what an agent concluded and what it
/// cost, so that the Agent Run detail page can reconstruct the run. It carries
/// no authority and triggers no trading action.
/// </remarks>
public sealed record AgentAnalysisCompleted : IntegrationEvent
{
    public override string EventType => EventTypes.Agents.AnalysisCompleted;

    public required Guid AgentRunIdValue { get; init; }

    /// <summary>Agent identity, e.g. <c>technical_agent</c>.</summary>
    public required string AgentName { get; init; }

    public required string AgentVersion { get; init; }

    /// <summary>Model identifier used for the run, or <c>stub</c> when deterministic.</summary>
    public required string Model { get; init; }

    public required string Symbol { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required int DurationMs { get; init; }

    /// <summary>Read-only tools the agent invoked, for permission auditing.</summary>
    public required IReadOnlyList<string> ToolsUsed { get; init; }

    /// <summary>Stable conclusion codes, e.g. <c>TREND_UP</c>.</summary>
    public required IReadOnlyList<string> ReasonCodes { get; init; }

    /// <summary>Agent confidence in the range 0 to 1 inclusive.</summary>
    public required decimal Confidence { get; init; }

    /// <summary>Token usage, for cost attribution. Zero in stub mode.</summary>
    public required int InputTokens { get; init; }

    public required int OutputTokens { get; init; }

    /// <summary>False when the run failed or produced output that failed schema validation.</summary>
    public required bool Succeeded { get; init; }

    public string? FailureReason { get; init; }
}

/// <summary>
/// The strategy agent has proposed a trade.
/// </summary>
/// <remarks>
/// <para>
/// This is the AI platform's single output into the trading path, and it is a
/// <em>proposal</em> in the strict sense. It is consumed by the Trading Service,
/// which validates it, derives its own position size deterministically, and
/// submits it to the risk gate. The proposal's own sizing hints are advisory and
/// may be reduced but never increased.
/// </para>
/// <para>
/// The AI platform cannot publish order events, holds no exchange credentials,
/// and has no execution tools. Everything downstream of this event is
/// deterministic.
/// </para>
/// </remarks>
public sealed record AgentSignalProposed : IntegrationEvent
{
    public override string EventType => EventTypes.Agents.SignalProposed;

    public required Guid ProposalId { get; init; }

    public required Guid AgentRunIdValue { get; init; }

    public required string Symbol { get; init; }

    /// <summary>One of <c>BUY</c>, <c>SELL</c> or <c>HOLD</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Aggregate confidence in the range 0 to 1 inclusive.</summary>
    public required decimal Confidence { get; init; }

    public required decimal Entry { get; init; }

    public decimal? StopLoss { get; init; }

    public decimal? TakeProfit { get; init; }

    public required IReadOnlyList<string> ReasonCodes { get; init; }

    public required IReadOnlyList<string> RiskNotes { get; init; }

    /// <summary>Per-agent contributions, retained so a proposal can be explained.</summary>
    public required IReadOnlyList<AgentContribution> Contributions { get; init; }
}

/// <summary>One agent's contribution to a combined proposal.</summary>
/// <param name="AgentName">Agent identity.</param>
/// <param name="Confidence">That agent's confidence, 0 to 1.</param>
/// <param name="ReasonCodes">That agent's conclusion codes.</param>
public sealed record AgentContribution(
    string AgentName,
    decimal Confidence,
    IReadOnlyList<string> ReasonCodes);
