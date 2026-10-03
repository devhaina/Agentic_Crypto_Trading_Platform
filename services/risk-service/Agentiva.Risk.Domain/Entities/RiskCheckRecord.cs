using System.Text.Json;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Domain.Evaluation;

namespace Agentiva.Risk.Domain.Entities;

/// <summary>
/// The persisted record of one risk evaluation.
/// </summary>
/// <remarks>
/// <para>
/// Written for every evaluation, approved or rejected. Storing rejections is the
/// point: the audit question "why was this trade not taken?" is asked at least
/// as often as "why was it taken?", and a system that only records what it did
/// cannot answer it.
/// </para>
/// <para>
/// The full check list is stored as JSON rather than normalised into a child
/// table. These rows are written once and read for display or audit; they are
/// never queried by individual check value, so a <c>jsonb</c> column keeps the
/// complete decision together and avoids a join on the hot write path.
/// </para>
/// </remarks>
public sealed class RiskCheckRecord : AggregateRoot<RiskCheckId>
{
    private RiskCheckRecord()
    {
        // EF Core materialisation.
    }

    private RiskCheckRecord(RiskCheckId id)
        : base(id)
    {
    }

    /// <summary>The intent that was evaluated.</summary>
    public Guid TradingIntentId { get; private set; }

    /// <summary>The policy applied.</summary>
    public RiskPolicyId RiskPolicyId { get; private set; }

    public Symbol Symbol { get; private set; }

    public OrderSide Side { get; private set; }

    public RiskDecision Decision { get; private set; }

    /// <summary>Quantity cleared for execution. Zero when rejected.</summary>
    public Quantity ApprovedQuantity { get; private set; }

    /// <summary>Order value at the effective entry price.</summary>
    public decimal ApprovedNotional { get; private set; }

    /// <summary>Entry price including the slippage assumption.</summary>
    public decimal EffectiveEntryPrice { get; private set; }

    public decimal StopLoss { get; private set; }

    public decimal? TakeProfit { get; private set; }

    /// <summary>Capital at risk if the stop is hit, fees included.</summary>
    public decimal RiskAmount { get; private set; }

    /// <summary>The risk budget the size was derived from.</summary>
    public decimal RiskBudget { get; private set; }

    /// <summary>Which constraint determined the final size.</summary>
    public string BindingConstraint { get; private set; } = string.Empty;

    /// <summary>Comma-separated failing codes, for cheap filtering and grouping.</summary>
    public string RejectionCodes { get; private set; } = string.Empty;

    /// <summary>Complete check list, serialised as JSON.</summary>
    public string ChecksJson { get; private set; } = "[]";

    /// <summary>Effective trading mode at evaluation time.</summary>
    public TradingMode TradingMode { get; private set; }

    /// <summary>End-to-end correlation identifier.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Originating AI agent run, when the intent traces back to one.</summary>
    public string? AgentRunId { get; private set; }

    /// <summary>Idempotency key of the originating command.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public DateTimeOffset EvaluatedAt { get; private set; }

    /// <summary>Creates a record from a completed evaluation.</summary>
    public static RiskCheckRecord FromEvaluation(
        RiskEvaluationRequest request,
        RiskEvaluationResult result,
        RiskPolicyId policyId,
        string correlationId,
        string? agentRunId,
        string idempotencyKey)
    {
        var record = new RiskCheckRecord(RiskCheckId.New())
        {
            TradingIntentId = request.TradingIntentId,
            RiskPolicyId = policyId,
            Symbol = request.Symbol,
            Side = request.Side,
            Decision = result.Decision,
            ApprovedQuantity = result.ApprovedQuantity,
            ApprovedNotional = result.Sizing?.Notional.Amount ?? 0m,
            EffectiveEntryPrice = result.Sizing?.EffectiveEntryPrice.Value ?? request.EntryPrice.Value,
            StopLoss = request.StopLoss?.Value ?? 0m,
            TakeProfit = request.TakeProfit?.Value,
            RiskAmount = result.Sizing?.RiskAmount.Amount ?? 0m,
            RiskBudget = result.Sizing?.RiskBudget.Amount ?? 0m,
            BindingConstraint = result.Sizing?.BindingConstraint.ToString() ?? "NotSized",
            RejectionCodes = string.Join(',', result.RejectionCodes),
            ChecksJson = JsonSerializer.Serialize(result.Checks, AgentivaJson.Options),
            TradingMode = request.TradingMode,
            CorrelationId = correlationId,
            AgentRunId = agentRunId,
            IdempotencyKey = idempotencyKey,
            EvaluatedAt = request.EvaluatedAt
        };

        return record;
    }
}
