using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Application.Abstractions;
using Agentiva.Risk.Domain.Entities;
using Agentiva.Risk.Domain.Evaluation;
using Microsoft.Extensions.Logging;

namespace Agentiva.Risk.Application.Evaluations;

/// <summary>
/// Assembles the evaluation inputs, runs the deterministic gate and records the
/// decision.
/// </summary>
/// <remarks>
/// The handler does the I/O and none of the deciding. Every limit comparison
/// lives in <see cref="DeterministicRiskEvaluator"/>, which is pure — so the
/// decision can be reproduced from the stored record without replaying this
/// handler's database and cache reads.
/// </remarks>
public sealed class EvaluateIntentCommandHandler(
    IRiskPolicyRepository policies,
    IRiskCheckRepository checks,
    IPlatformStateProvider platformState,
    IInstrumentPrecisionProvider precisionProvider,
    IUnitOfWork unitOfWork,
    ICorrelationContext correlation,
    IClock clock,
    ILogger<EvaluateIntentCommandHandler> logger)
    : IRequestHandler<EvaluateIntentCommand, Result<RiskEvaluationResponse>>
{
    public async Task<Result<RiskEvaluationResponse>> HandleAsync(
        EvaluateIntentCommand command,
        CancellationToken cancellationToken)
    {
        var symbol = Symbol.Create(command.Symbol);

        var side = string.Equals(command.Side, "BUY", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Buy
            : OrderSide.Sell;

        // --- Resolve the policy ------------------------------------------------
        var policy = command.RiskPolicyId is { } policyId
            ? await policies.GetByIdAsync(RiskPolicyId.From(policyId), cancellationToken)
            : await policies.GetDefaultAsync(cancellationToken);

        if (policy is null)
        {
            // Fail closed. Without a policy there are no limits, and treating
            // "no limits" as "approve" would turn a seeding mistake into
            // unbounded risk.
            logger.LogError(
                "No risk policy is available (requested {RequestedPolicyId}). Refusing to evaluate.",
                command.RiskPolicyId);

            return Result.Failure<RiskEvaluationResponse>(Error.Unavailable(
                "risk.policy_unavailable",
                "No active risk policy is configured. The risk gate refuses to evaluate without one."));
        }

        // --- Resolve exchange filters -------------------------------------------
        var precision = await precisionProvider.GetAsync(symbol, cancellationToken);

        if (precision is null)
        {
            // An unknown symbol cannot be sized: step size, tick size and
            // minimum notional are all unknown, so any quantity produced would
            // be rejected by the exchange at best.
            logger.LogWarning("Rejecting evaluation for unknown symbol {Symbol}.", symbol);

            return Result.Failure<RiskEvaluationResponse>(Error.Validation(
                RiskCheckCodes.UnknownSymbol,
                $"Symbol {symbol} has no configured exchange precision and cannot be sized."));
        }

        // --- Resolve platform state -----------------------------------------------
        var killSwitchEngaged = await platformState.IsKillSwitchEngagedAsync(cancellationToken);
        var tradingEnabled = await platformState.IsTradingEnabledAsync(cancellationToken);

        var quoteAsset = precision.QuoteAsset;

        var request = new RiskEvaluationRequest(
            TradingIntentId: command.TradingIntentId,
            Symbol: symbol,
            Side: side,
            EntryPrice: Price.Create(command.EntryPrice),
            StopLoss: command.StopLoss is { } stop ? Price.Create(stop) : null,
            TakeProfit: command.TakeProfit is { } target ? Price.Create(target) : null,

            // Confidence crosses the wire as a 0-1 fraction and becomes an
            // explicit Percentage here, so the comparison against the policy's
            // minimum cannot be off by a factor of a hundred.
            Confidence: Percentage.FromFraction(command.Confidence),

            Policy: policy,
            Precision: precision,
            PortfolioEquity: Money.Create(command.Portfolio.Equity, quoteAsset),
            AvailableBalance: Money.Create(command.Portfolio.AvailableBalance, quoteAsset),
            CurrentExposure: Money.Create(command.Portfolio.CurrentExposure, quoteAsset),
            CurrentSymbolExposure: Money.Create(command.Portfolio.CurrentSymbolExposure, quoteAsset),
            OpenPositionCount: command.Portfolio.OpenPositionCount,
            DailyPnl: Money.Create(command.Portfolio.DailyPnl, quoteAsset),
            SymbolVolatility: Percentage.FromPercent(command.SymbolVolatilityPercent),
            MarketDataAge: TimeSpan.FromSeconds(command.MarketDataAgeSeconds),
            IsExchangeAvailable: command.IsExchangeAvailable,
            IsKillSwitchEngaged: killSwitchEngaged,
            IsTradingEnabled: tradingEnabled,
            TradingMode: platformState.EffectiveTradingMode,
            HasDuplicateOpenOrder: command.HasDuplicateOpenOrder,
            EvaluatedAt: clock.UtcNow);

        // --- The gate ---------------------------------------------------------------
        var result = DeterministicRiskEvaluator.Evaluate(request);

        // --- Record the decision ------------------------------------------------------
        // Written for rejections too: "why was this trade not taken?" is asked
        // as often as the opposite, and only a stored record can answer it.
        var record = RiskCheckRecord.FromEvaluation(
            request,
            result,
            policy.Id,
            correlation.CorrelationId,
            correlation.AgentRunId,
            command.IdempotencyKey);

        checks.Add(record);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (result.IsApproved)
        {
            logger.LogInformation(
                "Risk APPROVED intent {TradingIntentId} for {Symbol} {Side}: quantity {Quantity}, "
                + "notional {Notional}, risk {RiskAmount} of budget {RiskBudget}, bound by {Constraint}.",
                command.TradingIntentId,
                symbol,
                side,
                result.ApprovedQuantity,
                result.Sizing!.Notional,
                result.Sizing.RiskAmount,
                result.Sizing.RiskBudget,
                result.Sizing.BindingConstraint);
        }
        else
        {
            logger.LogWarning(
                "Risk REJECTED intent {TradingIntentId} for {Symbol} {Side}: {RejectionCodes}",
                command.TradingIntentId,
                symbol,
                side,
                string.Join(", ", result.RejectionCodes));
        }

        return Result.Success(ToResponse(record, result, policy.Id));
    }

    private static RiskEvaluationResponse ToResponse(
        RiskCheckRecord record,
        RiskEvaluationResult result,
        RiskPolicyId policyId)
        => new(
            record.Id.Value,
            record.TradingIntentId,
            policyId.Value,
            result.Decision.ToString(),
            result.ApprovedQuantity.Value,
            record.ApprovedNotional,
            record.EffectiveEntryPrice,
            record.RiskAmount,
            record.RiskBudget,
            record.BindingConstraint,
            result.RejectionCodes,
            result.Checks
                .Select(c => new RiskCheckDto(
                    c.CheckName, c.Passed, c.Code, c.Detail, c.ObservedValue, c.LimitValue))
                .ToArray());
}
