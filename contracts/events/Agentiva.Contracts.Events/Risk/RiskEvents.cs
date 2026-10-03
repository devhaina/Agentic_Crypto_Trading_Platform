namespace Agentiva.Contracts.Events.Risk;

/// <summary>
/// The deterministic risk gate approved a trading intent.
/// </summary>
/// <remarks>
/// Carries the <em>approved</em> quantity, which may be lower than the quantity
/// requested — the risk service is permitted to reduce a position but never to
/// enlarge one. The Execution Service trades this quantity, not the original.
/// </remarks>
public sealed record RiskApproved : IntegrationEvent
{
    public override string EventType => EventTypes.Risk.Approved;

    public required Guid RiskCheckId { get; init; }

    public required Guid TradingIntentId { get; init; }

    public required Guid RiskPolicyId { get; init; }

    public required string Symbol { get; init; }

    public required string Side { get; init; }

    /// <summary>Quantity cleared for execution, after every cap and exchange filter.</summary>
    public required decimal ApprovedQuantity { get; init; }

    /// <summary>Order value in quote asset at the assumed entry price.</summary>
    public required decimal ApprovedNotional { get; init; }

    /// <summary>Entry price including the adverse slippage assumption.</summary>
    public required decimal EffectiveEntryPrice { get; init; }

    public required decimal StopLoss { get; init; }

    public decimal? TakeProfit { get; init; }

    /// <summary>Capital at risk if the stop is hit, in quote asset, fees included.</summary>
    public required decimal RiskAmount { get; init; }

    /// <summary>Every check that ran, with its outcome. The complete audit basis.</summary>
    public required IReadOnlyList<RiskCheckOutcome> Checks { get; init; }

    public required string IdempotencyKey { get; init; }
}

/// <summary>The deterministic risk gate rejected a trading intent.</summary>
public sealed record RiskRejected : IntegrationEvent
{
    public override string EventType => EventTypes.Risk.Rejected;

    public required Guid RiskCheckId { get; init; }

    public required Guid TradingIntentId { get; init; }

    public required Guid RiskPolicyId { get; init; }

    public required string Symbol { get; init; }

    /// <summary>
    /// Codes of the checks that failed, e.g. <c>risk.rejected.max_daily_loss</c>.
    /// Every failing check is reported, not just the first, so an operator sees
    /// the full picture in one place.
    /// </summary>
    public required IReadOnlyList<string> RejectionCodes { get; init; }

    public required IReadOnlyList<RiskCheckOutcome> Checks { get; init; }
}

/// <summary>Outcome of a single named risk check.</summary>
/// <param name="CheckName">Check identity, e.g. <c>MaxDailyLoss</c>.</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Code">Stable reason code.</param>
/// <param name="Detail">Operator-facing explanation.</param>
/// <param name="ObservedValue">The measured value, as a string to preserve decimal precision.</param>
/// <param name="LimitValue">The configured limit the value was compared against.</param>
public sealed record RiskCheckOutcome(
    string CheckName,
    bool Passed,
    string Code,
    string Detail,
    string? ObservedValue,
    string? LimitValue);

/// <summary>
/// A portfolio-level risk limit has been breached.
/// </summary>
/// <remarks>
/// Distinct from a rejection. A rejection stops one trade; a breach means the
/// account is already outside its limits — for instance the daily loss cap was
/// crossed by market movement on open positions — and typically trips the kill
/// switch.
/// </remarks>
public sealed record RiskLimitBreached : IntegrationEvent
{
    public override string EventType => EventTypes.Risk.LimitBreached;

    public required Guid TradingAccountId { get; init; }

    /// <summary>Which limit was breached, e.g. <c>MaxDailyLoss</c>.</summary>
    public required string LimitName { get; init; }

    public required string ObservedValue { get; init; }

    public required string LimitValue { get; init; }

    /// <summary><c>WARNING</c> or <c>CRITICAL</c>. Critical trips the kill switch.</summary>
    public required string Severity { get; init; }
}
