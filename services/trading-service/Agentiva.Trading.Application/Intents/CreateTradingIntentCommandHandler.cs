using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.Application.Configuration;
using Agentiva.Trading.Application.Abstractions;
using Agentiva.Trading.Domain.Intents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.Trading.Application.Intents;

/// <summary>
/// Drives the trading workflow: record the intent, gather inputs, submit to the
/// risk gate, record the decision.
/// </summary>
/// <remarks>
/// <para>
/// The ordering here is deliberate and is the point of the whole class. The
/// intent is persisted <em>before</em> the risk gate is called, so that if this
/// process dies mid-workflow the intent exists in a known, non-executable state
/// rather than vanishing. The risk decision is then recorded against it in a
/// second transaction.
/// </para>
/// <para>
/// Every failure path leaves the intent non-executable. There is no branch in
/// which an unreachable risk gate, a timeout or an unexpected response results
/// in an intent that execution will pick up.
/// </para>
/// </remarks>
public sealed class CreateTradingIntentCommandHandler(
    ITradingIntentRepository intents,
    IRiskServiceClient riskService,
    IExecutionServiceClient executionService,
    IPortfolioSnapshotProvider portfolio,
    IMarketConditionProvider marketConditions,
    IUnitOfWork unitOfWork,
    ICorrelationContext correlation,
    IOptions<TradingOptions> tradingOptions,
    IClock clock,
    ILogger<CreateTradingIntentCommandHandler> logger)
    : IRequestHandler<CreateTradingIntentCommand, Result<TradingIntentResponse>>
{
    private readonly TradingOptions _trading = tradingOptions.Value;

    public async Task<Result<TradingIntentResponse>> HandleAsync(
        CreateTradingIntentCommand command,
        CancellationToken cancellationToken)
    {
        var symbol = Symbol.Create(command.Symbol);

        var side = string.Equals(command.Side, "BUY", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Buy
            : OrderSide.Sell;

        var source = Enum.Parse<TradingIntentSource>(command.Source, ignoreCase: true);

        // --- 1. Establish an entry price --------------------------------------
        var conditions = await marketConditions.GetAsync(symbol.Value, cancellationToken);

        var entryPriceValue = command.EntryPrice ?? conditions.LastPrice;

        if (entryPriceValue is null or <= 0m)
        {
            // No supplied price and no market price to fall back on. Refused
            // rather than guessed: an intent with an invented entry would
            // produce a meaningless stop distance and therefore a meaningless
            // position size.
            logger.LogWarning(
                "Refusing intent for {Symbol}: no entry price was supplied and no market price is available.",
                symbol);

            return Result.Failure<TradingIntentResponse>(Error.Validation(
                "trading.intent.no_entry_price",
                $"No entry price was supplied for {symbol} and no current market price is available."));
        }

        var entryPrice = Price.Create(entryPriceValue.Value);

        // --- 2. Record the intent ----------------------------------------------
        var intent = TradingIntent.Create(
            accountId: TradingAccountId.From(command.TradingAccountId),
            symbol: symbol,
            side: side,
            requestedQuantity: Quantity.Create(command.Quantity),
            entryPrice: entryPrice,
            stopLoss: command.StopLoss is { } stop ? Price.Create(stop) : null,
            takeProfit: command.TakeProfit is { } target ? Price.Create(target) : null,
            confidence: Percentage.FromFraction(command.Confidence),
            source: source,
            tradingMode: _trading.EffectiveMode,
            signalId: command.SignalId is { } sig ? SignalId.From(sig) : null,
            strategyId: command.StrategyId is { } strat ? StrategyId.From(strat) : null,
            agentRunId: command.AgentRunId is { } run ? AgentRunId.From(run) : null,
            idempotencyKey: command.IdempotencyKey,
            correlationId: correlation.CorrelationId,
            createdBy: command.CreatedBy,
            now: clock.UtcNow);

        intents.Add(intent);
        intent.SubmitToRisk(clock.UtcNow);

        // Committed before the risk call. A crash after this point leaves a
        // visible intent in AwaitingRisk, which an operator or a future
        // reconciliation sweep can resolve — far better than losing the record
        // of a trade the platform was about to attempt.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Recorded trading intent {TradingIntentId} from {Source} for {Symbol} {Side} {Quantity} "
            + "in {TradingMode} mode; submitting to the risk gate.",
            intent.Id,
            source,
            symbol,
            side,
            command.Quantity,
            _trading.EffectiveMode);

        // --- 3. Gather risk inputs ------------------------------------------------
        var snapshotResult = await portfolio.GetAsync(command.TradingAccountId, symbol.Value, cancellationToken);

        if (snapshotResult.IsFailure)
        {
            // Fail closed, the same as an unreachable risk gate: a portfolio
            // snapshot is a risk input, and a fabricated one would feed the
            // gate false confidence rather than no confidence.
            intent.MarkRiskUnavailable(snapshotResult.Error.ToString(), clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogError(
                "Portfolio Service unavailable for intent {TradingIntentId}: {Reason}. "
                + "The intent will not be executed.",
                intent.Id,
                snapshotResult.Error);

            return Result.Success(ToResponse(intent));
        }

        var snapshot = snapshotResult.Value;

        // The real duplicate-order check: ask the Execution Service, which
        // owns the order store, rather than the Phase 1 hard-coded false. An
        // unreachable check fails closed — treated as "open" rather than
        // "clear" — because understating risk input is worse than an
        // occasional unnecessary rejection; the deterministic client order id
        // remains the last line of defence regardless.
        var openOrderCheck = await executionService.HasOpenOrderAsync(
            symbol.Value, side.ToString().ToUpperInvariant(), cancellationToken);

        if (openOrderCheck.IsFailure)
        {
            logger.LogWarning(
                "Could not reach the Execution Service to check for an open order on {Symbol} {Side}: "
                + "{Reason}. Treating the duplicate-order check as failed (open) rather than clear.",
                symbol,
                side,
                openOrderCheck.Error);
        }

        var hasDuplicateOpenOrder = openOrderCheck.IsFailure || openOrderCheck.Value;

        var riskRequest = new RiskEvaluationRequestDto(
            TradingIntentId: intent.Id.Value,
            Symbol: symbol.Value,
            Side: side.ToString().ToUpperInvariant(),
            EntryPrice: entryPrice.Value,
            StopLoss: command.StopLoss,
            TakeProfit: command.TakeProfit,
            Confidence: command.Confidence,
            Portfolio: snapshot,
            SymbolVolatilityPercent: conditions.VolatilityPercent,
            MarketDataAgeSeconds: conditions.MarketDataAgeSeconds,
            IsExchangeAvailable: conditions.IsExchangeAvailable,
            HasDuplicateOpenOrder: hasDuplicateOpenOrder);

        // The risk gate's idempotency key is derived from this command's key, so
        // a replay of the whole command replays the same evaluation rather than
        // producing a second decision.
        var riskIdempotencyKey = $"{command.IdempotencyKey}:risk";

        var decision = await riskService.EvaluateAsync(riskRequest, riskIdempotencyKey, cancellationToken);

        // --- 4. Record the decision -------------------------------------------------
        if (decision.IsFailure)
        {
            // Fail closed. An unreachable gate is never an approval, and the
            // intent is parked in a dedicated state so an outage is not later
            // mistaken for a policy rejection.
            intent.MarkRiskUnavailable(decision.Error.ToString(), clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogError(
                "Risk gate unavailable for intent {TradingIntentId}: {Reason}. "
                + "The intent will not be executed.",
                intent.Id,
                decision.Error);

            return Result.Success(ToResponse(intent));
        }

        var outcome = decision.Value;

        if (!outcome.IsApproved)
        {
            intent.RejectRisk(
                RiskCheckId.From(outcome.RiskCheckId),
                outcome.RejectionCodes,
                clock.UtcNow);

            logger.LogWarning(
                "Intent {TradingIntentId} rejected by the risk gate: {RejectionCodes}",
                intent.Id,
                string.Join(", ", outcome.RejectionCodes));

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToResponse(intent));
        }

        intent.ApproveRisk(
            RiskCheckId.From(outcome.RiskCheckId),
            Quantity.Create(outcome.ApprovedQuantity),
            clock.UtcNow);

        logger.LogInformation(
            "Intent {TradingIntentId} approved: {ApprovedQuantity} of {RequestedQuantity} requested, "
            + "notional {Notional}, risk {RiskAmount}.",
            intent.Id,
            outcome.ApprovedQuantity,
            command.Quantity,
            outcome.ApprovedNotional,
            outcome.RiskAmount);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // --- 5. Hand the approved intent to the Execution Service -----------------
        var orderRequest = new ExecutionOrderRequestDto(
            TradingIntentId: intent.Id.Value,
            RiskCheckId: outcome.RiskCheckId,
            TradingAccountId: command.TradingAccountId,
            Symbol: symbol.Value,
            Side: side.ToString().ToUpperInvariant(),
            OrderType: "MARKET",
            Quantity: outcome.ApprovedQuantity,
            LimitPrice: null,
            ReferencePrice: outcome.EffectiveEntryPrice);

        var executionIdempotencyKey = $"{command.IdempotencyKey}:order";

        var placement = await executionService.SubmitOrderAsync(
            orderRequest, executionIdempotencyKey, cancellationToken);

        if (placement.IsFailure)
        {
            // The risk gate approved the trade but it could not be handed off.
            // Failed, not RiskUnavailable: a decision was made, only the
            // execution step itself did not complete.
            intent.Fail(placement.Error.ToString(), clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogError(
                "Intent {TradingIntentId} was approved but could not be executed: {Reason}",
                intent.Id,
                placement.Error);

            return Result.Success(ToResponse(intent));
        }

        var order = placement.Value;
        intent.BeginExecution(OrderId.From(order.OrderId), clock.UtcNow);

        if (order.IsFilled)
        {
            intent.CompleteExecution(clock.UtcNow);
        }
        else if (!order.IsResting)
        {
            // Rejected or indeterminate at the exchange. The intent reached
            // execution but did not result in a position.
            intent.Fail($"Order {order.OrderId} reached status {order.Status}.", clock.UtcNow);
        }

        // Else: resting on the book (Submitted/PartiallyFilled). The intent
        // stays Executing — there is no order-event consumer yet to advance
        // it to Executed asynchronously; see
        // docs/architecture/known-limitations.md.

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Intent {TradingIntentId} executed as order {OrderId}: {Status}.",
            intent.Id,
            order.OrderId,
            order.Status);

        return Result.Success(ToResponse(intent));
    }

    internal static TradingIntentResponse ToResponse(TradingIntent intent)
        => new(
            intent.Id.Value,
            intent.Symbol.Value,
            intent.Side.ToString().ToUpperInvariant(),
            intent.Status.ToString(),
            intent.RequestedQuantity.Value,
            intent.ApprovedQuantity.Value,
            intent.EntryPrice.Value,
            intent.StopLoss?.Value,
            intent.TakeProfit?.Value,
            intent.RiskCheckId?.Value,
            string.IsNullOrEmpty(intent.RejectionCodes)
                ? []
                : intent.RejectionCodes.Split(',', StringSplitOptions.RemoveEmptyEntries),
            intent.TradingMode.ToString().ToUpperInvariant(),
            intent.Source.ToString().ToUpperInvariant(),
            intent.FailureReason,
            intent.CreatedAt,
            intent.CorrelationId);
}
