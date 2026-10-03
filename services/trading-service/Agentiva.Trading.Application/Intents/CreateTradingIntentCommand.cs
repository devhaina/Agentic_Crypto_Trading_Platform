using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;

namespace Agentiva.Trading.Application.Intents;

/// <summary>
/// Creates a trading intent and runs it through the risk gate.
/// </summary>
/// <remarks>
/// The single entry point into the trading workflow, used by strategy signals,
/// AI proposals and manual requests alike. All three are treated identically
/// from here on, which is what makes the risk gate unbypassable: there is no
/// second path.
/// </remarks>
/// <param name="IdempotencyKey">Key guarding the whole intent-to-order path.</param>
/// <param name="TradingAccountId">The account to trade on.</param>
/// <param name="Symbol">Trading pair, e.g. <c>BTCUSDT</c>.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="Quantity">
/// Requested quantity. An upper bound only: the risk gate derives its own size
/// and may approve less, never more.
/// </param>
/// <param name="EntryPrice">Proposed entry price. Omit to use the last traded price.</param>
/// <param name="StopLoss">Protective stop. Required by the default risk policy.</param>
/// <param name="TakeProfit">Target. Required by the default risk policy.</param>
/// <param name="Confidence">Confidence as a fraction, 0 to 1.</param>
/// <param name="Source"><c>STRATEGY</c>, <c>AGENT</c> or <c>MANUAL</c>.</param>
/// <param name="SignalId">Originating signal, when there was one.</param>
/// <param name="StrategyId">Originating strategy, when there was one.</param>
/// <param name="AgentRunId">Originating AI agent run, when there was one.</param>
/// <param name="CreatedBy">Acting user id, or <c>SYSTEM</c>.</param>
public sealed record CreateTradingIntentCommand(
    string IdempotencyKey,
    Guid TradingAccountId,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal Confidence,
    string Source,
    Guid? SignalId,
    Guid? StrategyId,
    Guid? AgentRunId,
    string CreatedBy) : IFinancialCommand<Result<TradingIntentResponse>>;

/// <summary>A trading intent and its current workflow state.</summary>
/// <param name="TradingIntentId">Intent identity.</param>
/// <param name="Symbol">Trading pair.</param>
/// <param name="Side">Trade direction.</param>
/// <param name="Status">Workflow state.</param>
/// <param name="RequestedQuantity">Quantity asked for.</param>
/// <param name="ApprovedQuantity">Quantity the risk gate cleared.</param>
/// <param name="EntryPrice">Proposed entry price.</param>
/// <param name="StopLoss">Protective stop.</param>
/// <param name="TakeProfit">Target.</param>
/// <param name="RiskCheckId">The risk evaluation that decided this intent.</param>
/// <param name="RejectionCodes">Failing risk codes. Empty unless rejected.</param>
/// <param name="TradingMode">Effective trading mode.</param>
/// <param name="Source">What created the intent.</param>
/// <param name="FailureReason">Why the intent failed, when it did.</param>
/// <param name="CreatedAt">When the intent was recorded.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record TradingIntentResponse(
    Guid TradingIntentId,
    string Symbol,
    string Side,
    string Status,
    decimal RequestedQuantity,
    decimal ApprovedQuantity,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    Guid? RiskCheckId,
    IReadOnlyList<string> RejectionCodes,
    string TradingMode,
    string Source,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    string CorrelationId);
