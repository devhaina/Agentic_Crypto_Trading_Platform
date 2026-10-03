namespace Agentiva.Trading.Domain.Intents;

/// <summary>
/// Lifecycle of a trading intent.
/// </summary>
/// <remarks>
/// The intent is the platform's durable record of "we wanted to trade", and it
/// survives every outcome including rejection. That is what makes a rejected
/// trade auditable: without a persisted intent, the only evidence that the
/// platform considered and declined a trade would be a log line.
/// </remarks>
public enum TradingIntentStatus
{
    /// <summary>Recorded and validated; not yet submitted to the risk gate.</summary>
    Created = 1,

    /// <summary>Submitted to the risk gate; awaiting a decision.</summary>
    AwaitingRisk = 2,

    /// <summary>Approved by the risk gate and ready for execution.</summary>
    RiskApproved = 3,

    /// <summary>Rejected by the risk gate. Terminal.</summary>
    RiskRejected = 4,

    /// <summary>Handed to the Execution Service.</summary>
    Executing = 5,

    /// <summary>The resulting order filled. Terminal.</summary>
    Executed = 6,

    /// <summary>Execution failed for a known reason. Terminal.</summary>
    Failed = 7,

    /// <summary>Cancelled before execution. Terminal.</summary>
    Cancelled = 8,

    /// <summary>
    /// The risk gate could not be reached, so no decision exists.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="RiskRejected"/> and deliberately <em>not</em>
    /// treated as one. A rejection is a decision; this is the absence of one.
    /// It never progresses to execution — failing closed — but it is recorded
    /// separately so that an outage is not mistaken for a policy breach when
    /// the day's rejections are reviewed.
    /// </remarks>
    RiskUnavailable = 9
}

/// <summary>What created a trading intent.</summary>
public enum TradingIntentSource
{
    /// <summary>A deterministic strategy signal.</summary>
    Strategy = 1,

    /// <summary>An AI agent proposal. Advisory only; still fully risk-gated.</summary>
    Agent = 2,

    /// <summary>A human operator.</summary>
    Manual = 3
}
