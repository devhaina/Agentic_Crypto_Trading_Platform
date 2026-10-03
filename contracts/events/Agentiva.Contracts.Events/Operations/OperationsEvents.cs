namespace Agentiva.Contracts.Events.Operations;

/// <summary>
/// Internal state and exchange state disagree.
/// </summary>
/// <remarks>
/// Always treated as critical. A balance or position mismatch means the
/// platform's view of reality is wrong, so every risk calculation downstream of
/// it is also wrong. The platform stops trading and waits for a human; it never
/// silently adjusts its own books to match.
/// </remarks>
public sealed record ReconciliationFailed : IntegrationEvent
{
    public override string EventType => EventTypes.Operations.ReconciliationFailed;

    public required Guid ReconciliationResultId { get; init; }

    public required Guid TradingAccountId { get; init; }

    /// <summary>What disagreed: <c>BALANCE</c>, <c>ORDER</c>, <c>FILL</c> or <c>POSITION</c>.</summary>
    public required string MismatchType { get; init; }

    public required IReadOnlyList<ReconciliationMismatch> Mismatches { get; init; }

    public required DateTimeOffset CheckedAt { get; init; }
}

/// <summary>One discrepancy between internal and exchange state.</summary>
/// <param name="Scope">What was compared, e.g. a symbol or asset code.</param>
/// <param name="Field">The field that differed.</param>
/// <param name="InternalValue">Platform value, as a string to preserve precision.</param>
/// <param name="ExchangeValue">Exchange value, as a string to preserve precision.</param>
public sealed record ReconciliationMismatch(
    string Scope,
    string Field,
    string InternalValue,
    string ExchangeValue);

/// <summary>Internal state matched exchange state.</summary>
public sealed record ReconciliationSucceeded : IntegrationEvent
{
    public override string EventType => EventTypes.Operations.ReconciliationSucceeded;

    public required Guid ReconciliationResultId { get; init; }

    public required Guid TradingAccountId { get; init; }

    public required int ItemsChecked { get; init; }

    public required DateTimeOffset CheckedAt { get; init; }
}

/// <summary>The global kill switch engaged.</summary>
public sealed record KillSwitchActivated : IntegrationEvent
{
    public override string EventType => EventTypes.Operations.KillSwitchActivated;

    /// <summary>Trigger code, e.g. <c>daily_loss_exceeded</c> or <c>market_data_stale</c>.</summary>
    public required string Trigger { get; init; }

    public required string Detail { get; init; }

    /// <summary><c>SYSTEM</c> or an operator user id.</summary>
    public required string ActivatedBy { get; init; }

    /// <summary>Whether resting orders eligible for cancellation were cancelled.</summary>
    public required bool PendingOrdersCancelled { get; init; }

    /// <summary>
    /// Always false unless explicitly configured and independently risk-approved.
    /// Automatic liquidation can turn a transient fault into a realised loss.
    /// </summary>
    public required bool PositionsLiquidated { get; init; }
}

/// <summary>The global kill switch was released by an operator.</summary>
public sealed record KillSwitchDeactivated : IntegrationEvent
{
    public override string EventType => EventTypes.Operations.KillSwitchDeactivated;

    /// <summary>Operator user id. The system never releases its own kill switch.</summary>
    public required string DeactivatedBy { get; init; }

    public required string Justification { get; init; }
}

/// <summary>An operational alert for the notification service.</summary>
public sealed record AlertRaised : IntegrationEvent
{
    public override string EventType => EventTypes.Operations.AlertRaised;

    public required Guid SystemAlertId { get; init; }

    /// <summary><c>INFO</c>, <c>WARNING</c> or <c>CRITICAL</c>.</summary>
    public required string Severity { get; init; }

    /// <summary>Stable alert code for routing and de-duplication.</summary>
    public required string Code { get; init; }

    public required string Title { get; init; }

    public required string Detail { get; init; }

    /// <summary>Service that raised the alert.</summary>
    public required string Source { get; init; }
}

/// <summary>
/// An audited decision occurred.
/// </summary>
/// <remarks>
/// Published by every service at each financial decision point so that the audit
/// service can answer, from one table, why a trade was proposed, why it was
/// approved or rejected, what was sent to the exchange, and what came back.
/// </remarks>
public sealed record AuditEventRecorded : IntegrationEvent
{
    public override string EventType => EventTypes.Operations.AuditEventRecorded;

    public required Guid AuditEventId { get; init; }

    /// <summary>Decision category, e.g. <c>RISK_DECISION</c> or <c>ORDER_SUBMISSION</c>.</summary>
    public required string Category { get; init; }

    /// <summary>What happened, e.g. <c>RISK_APPROVED</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Service that produced the record.</summary>
    public required string Source { get; init; }

    /// <summary>Acting user id, or <c>SYSTEM</c> for automated decisions.</summary>
    public required string ActorId { get; init; }

    /// <summary>Stable codes explaining the decision.</summary>
    public required IReadOnlyList<string> ReasonCodes { get; init; }

    /// <summary>
    /// Identifiers tying this record to the rest of the chain — intent, risk
    /// check, order, exchange order. Present keys depend on the category.
    /// </summary>
    public required IReadOnlyDictionary<string, string> References { get; init; }
}
