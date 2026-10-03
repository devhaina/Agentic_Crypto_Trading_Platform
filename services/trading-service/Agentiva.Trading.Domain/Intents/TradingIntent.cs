using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Trading.Domain.Intents;

/// <summary>
/// A recorded decision to attempt a trade, and the workflow state around it.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate at the centre of the trading workflow. It is created before any
/// risk decision and before any order, which is what allows the platform to
/// answer "why was this trade not taken?" — the record of wanting to trade
/// outlives the rejection.
/// </para>
/// <para>
/// State transitions are enforced here rather than by the caller. An intent
/// cannot reach <see cref="TradingIntentStatus.Executing"/> without first being
/// <see cref="TradingIntentStatus.RiskApproved"/>, so no code path — including a
/// future one written by someone who has not read this file — can route around
/// the risk gate.
/// </para>
/// </remarks>
public sealed class TradingIntent : AggregateRoot<TradingIntentId>
{
    private TradingIntent()
    {
        // EF Core materialisation.
    }

    private TradingIntent(TradingIntentId id)
        : base(id)
    {
    }

    public TradingAccountId TradingAccountId { get; private set; }

    public Symbol Symbol { get; private set; }

    public OrderSide Side { get; private set; }

    /// <summary>
    /// Quantity the caller asked for.
    /// </summary>
    /// <remarks>
    /// Retained unchanged for audit even when risk approves less. Comparing it
    /// against <see cref="ApprovedQuantity"/> shows how often, and by how much,
    /// the gate reduces requests.
    /// </remarks>
    public Quantity RequestedQuantity { get; private set; }

    /// <summary>Quantity the risk gate cleared. Never greater than requested.</summary>
    public Quantity ApprovedQuantity { get; private set; }

    public Price EntryPrice { get; private set; }

    public Price? StopLoss { get; private set; }

    public Price? TakeProfit { get; private set; }

    /// <summary>Signal confidence as a fraction, 0 to 1.</summary>
    public Percentage Confidence { get; private set; }

    public TradingIntentStatus Status { get; private set; } = TradingIntentStatus.Created;

    public TradingIntentSource Source { get; private set; }

    public TradingMode TradingMode { get; private set; }

    /// <summary>Originating deterministic signal, when there was one.</summary>
    public SignalId? SignalId { get; private set; }

    public StrategyId? StrategyId { get; private set; }

    /// <summary>Originating AI agent run, when the intent traces back to one.</summary>
    public AgentRunId? AgentRunId { get; private set; }

    /// <summary>The risk evaluation that decided this intent.</summary>
    public RiskCheckId? RiskCheckId { get; private set; }

    /// <summary>Failing risk codes, comma-separated. Empty unless rejected.</summary>
    public string RejectionCodes { get; private set; } = string.Empty;

    /// <summary>The order created from this intent, once one exists.</summary>
    public OrderId? OrderId { get; private set; }

    /// <summary>Idempotency key guarding the whole intent-to-order path.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Acting user id, or <c>SYSTEM</c> for an automated intent.</summary>
    public string CreatedBy { get; private set; } = "SYSTEM";

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Why the intent reached a failure or unavailable state.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Records a new trading intent and raises its creation event.</summary>
    public static TradingIntent Create(
        TradingAccountId accountId,
        Symbol symbol,
        OrderSide side,
        Quantity requestedQuantity,
        Price entryPrice,
        Price? stopLoss,
        Price? takeProfit,
        Percentage confidence,
        TradingIntentSource source,
        TradingMode tradingMode,
        SignalId? signalId,
        StrategyId? strategyId,
        AgentRunId? agentRunId,
        string idempotencyKey,
        string correlationId,
        string createdBy,
        DateTimeOffset now)
    {
        if (requestedQuantity.IsZero)
        {
            throw new DomainException(
                "trading.intent.zero_quantity", "A trading intent must request a non-zero quantity.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException(
                "trading.intent.missing_idempotency_key",
                "A trading intent must carry an idempotency key.");
        }

        var intent = new TradingIntent(TradingIntentId.New())
        {
            TradingAccountId = accountId,
            Symbol = symbol,
            Side = side,
            RequestedQuantity = requestedQuantity,
            ApprovedQuantity = Quantity.Zero,
            EntryPrice = entryPrice,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            Confidence = confidence,
            Status = TradingIntentStatus.Created,
            Source = source,
            TradingMode = tradingMode,
            SignalId = signalId,
            StrategyId = strategyId,
            AgentRunId = agentRunId,
            IdempotencyKey = idempotencyKey,
            CorrelationId = correlationId,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        };

        intent.Raise(new TradeIntentCreatedDomainEvent(
            now,
            intent.Id.Value,
            accountId.Value,
            symbol.Value,
            side.ToString().ToUpperInvariant(),
            requestedQuantity.Value,
            entryPrice.Value,
            stopLoss?.Value,
            takeProfit?.Value,
            signalId?.Value,
            strategyId?.Value,
            source.ToString().ToUpperInvariant(),
            tradingMode.ToString().ToUpperInvariant(),
            idempotencyKey));

        return intent;
    }

    /// <summary>Marks the intent as submitted to the risk gate.</summary>
    public void SubmitToRisk(DateTimeOffset now)
    {
        RequireStatus(now, TradingIntentStatus.AwaitingRisk, TradingIntentStatus.Created);
        Status = TradingIntentStatus.AwaitingRisk;
    }

    /// <summary>Records a risk approval and the quantity cleared.</summary>
    /// <exception cref="DomainException">
    /// The approved quantity exceeds the requested quantity.
    /// </exception>
    public void ApproveRisk(RiskCheckId riskCheckId, Quantity approvedQuantity, DateTimeOffset now)
    {
        RequireStatus(now, TradingIntentStatus.RiskApproved,
            TradingIntentStatus.Created, TradingIntentStatus.AwaitingRisk);

        // The risk gate may reduce a position but never enlarge one. Enforced
        // on the aggregate as well as in the gate, so a bug or a tampered
        // response cannot inflate a position after the decision was made.
        if (approvedQuantity > RequestedQuantity)
        {
            throw new DomainException(
                "trading.intent.approved_exceeds_requested",
                $"Risk approved {approvedQuantity} but only {RequestedQuantity} was requested. "
                + "The risk gate may reduce a position, never increase it.");
        }

        if (approvedQuantity.IsZero)
        {
            throw new DomainException(
                "trading.intent.approved_zero",
                "A risk approval must clear a non-zero quantity.");
        }

        RiskCheckId = riskCheckId;
        ApprovedQuantity = approvedQuantity;
        Status = TradingIntentStatus.RiskApproved;
        UpdatedAt = now;
    }

    /// <summary>Records a risk rejection and the codes that caused it.</summary>
    public void RejectRisk(RiskCheckId riskCheckId, IEnumerable<string> rejectionCodes, DateTimeOffset now)
    {
        RequireStatus(now, TradingIntentStatus.RiskRejected,
            TradingIntentStatus.Created, TradingIntentStatus.AwaitingRisk);

        RiskCheckId = riskCheckId;
        RejectionCodes = string.Join(',', rejectionCodes);
        ApprovedQuantity = Quantity.Zero;
        Status = TradingIntentStatus.RiskRejected;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records that the risk gate could not be reached.
    /// </summary>
    /// <remarks>
    /// Fails closed: the intent stops here and never advances to execution.
    /// Tracked separately from a rejection so that a risk-service outage is not
    /// counted as a policy breach in the day's review.
    /// </remarks>
    public void MarkRiskUnavailable(string reason, DateTimeOffset now)
    {
        RequireStatus(now, TradingIntentStatus.RiskUnavailable,
            TradingIntentStatus.Created, TradingIntentStatus.AwaitingRisk);

        Status = TradingIntentStatus.RiskUnavailable;
        FailureReason = Truncate(reason, 1000);
        ApprovedQuantity = Quantity.Zero;
        UpdatedAt = now;
    }

    /// <summary>Marks the intent as handed to the Execution Service.</summary>
    /// <exception cref="DomainException">The intent has not been risk-approved.</exception>
    public void BeginExecution(OrderId orderId, DateTimeOffset now)
    {
        // The structural guarantee that nothing reaches an exchange unapproved.
        if (Status != TradingIntentStatus.RiskApproved)
        {
            throw new DomainException(
                "trading.intent.execution_without_approval",
                $"Intent {Id} is {Status} and cannot be executed. Only a risk-approved intent "
                + "may proceed to execution.");
        }

        OrderId = orderId;
        Status = TradingIntentStatus.Executing;
        UpdatedAt = now;
    }

    /// <summary>Marks execution as complete.</summary>
    public void CompleteExecution(DateTimeOffset now)
    {
        RequireStatus(now, TradingIntentStatus.Executed, TradingIntentStatus.Executing);
        Status = TradingIntentStatus.Executed;
    }

    /// <summary>Marks the intent as failed.</summary>
    public void Fail(string reason, DateTimeOffset now)
    {
        Status = TradingIntentStatus.Failed;
        FailureReason = Truncate(reason, 1000);
        UpdatedAt = now;
    }

    /// <summary>Cancels an intent that has not yet been executed.</summary>
    /// <exception cref="DomainException">The intent is already in a terminal state.</exception>
    public void Cancel(string reason, DateTimeOffset now)
    {
        if (Status is TradingIntentStatus.Executed
            or TradingIntentStatus.Failed
            or TradingIntentStatus.Cancelled
            or TradingIntentStatus.RiskRejected)
        {
            throw new DomainException(
                "trading.intent.already_terminal",
                $"Intent {Id} is already {Status} and cannot be cancelled.");
        }

        Status = TradingIntentStatus.Cancelled;
        FailureReason = Truncate(reason, 1000);
        UpdatedAt = now;
    }

    private void RequireStatus(
        DateTimeOffset now, TradingIntentStatus target, params TradingIntentStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(Status))
        {
            throw new DomainException(
                "trading.intent.invalid_transition",
                $"Intent {Id} cannot move from {Status} to {target}. "
                + $"Allowed prior states: {string.Join(", ", allowedFrom)}.");
        }

        UpdatedAt = now;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
