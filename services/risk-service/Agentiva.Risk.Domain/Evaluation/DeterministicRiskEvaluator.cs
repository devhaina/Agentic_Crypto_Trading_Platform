using System.Globalization;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Domain.Sizing;

namespace Agentiva.Risk.Domain.Evaluation;

/// <summary>
/// The platform's hard risk gate. Pure, deterministic, and the last decision
/// point before an order can exist.
/// </summary>
/// <remarks>
/// <para>
/// Nothing upstream can bypass this. An AI agent's proposal, a strategy signal
/// and a manual request all arrive as the same <see cref="RiskEvaluationRequest"/>
/// and are measured against the same limits. The evaluator contains no model
/// call, no network I/O and no randomness: same inputs, same decision, every
/// time. That property is what lets a rejection be explained months later from
/// the audit record alone.
/// </para>
/// <para>
/// Two design choices are worth calling out.
/// </para>
/// <para>
/// <b>Every check runs; the first failure does not short-circuit.</b> Returning
/// on the first failing check would be marginally faster and considerably less
/// useful: an operator investigating a rejection wants the complete picture, not
/// to fix one limit and rediscover the next. The only exception is the group of
/// integrity gates below, where continuing would mean computing a size from
/// data already known to be untrustworthy.
/// </para>
/// <para>
/// <b>Sizing happens between the two groups of checks.</b> Some limits are
/// independent of size (confidence, staleness, kill switch) and must be
/// evaluated first so that a stale-price trade is never sized at all. The rest
/// (balance, exposure, concentration, exchange minimums) can only be judged
/// against a concrete quantity, so they run afterwards against the number the
/// sizer actually produced.
/// </para>
/// </remarks>
public static class DeterministicRiskEvaluator
{
    /// <summary>Evaluates a trading intent against the policy in force.</summary>
    public static RiskEvaluationResult Evaluate(RiskEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var checks = new List<RiskCheckOutcome>(16);
        var policy = request.Policy;

        // =====================================================================
        // Group 1 — integrity gates.
        //
        // These short-circuit. If the kill switch is engaged, the exchange is
        // unreachable or prices are stale, there is no safe way to size a
        // position: the inputs to the arithmetic are themselves unreliable.
        // Proceeding would produce a confident-looking number derived from
        // data the platform has already established it cannot trust.
        // =====================================================================

        if (request.IsKillSwitchEngaged)
        {
            checks.Add(RiskCheckOutcome.Fail(
                RiskCheckNames.KillSwitch,
                RiskCheckCodes.KillSwitch,
                "The global kill switch is engaged. No new orders are admitted."));

            return Reject(checks, sizing: null);
        }

        checks.Add(RiskCheckOutcome.Pass(RiskCheckNames.KillSwitch));

        if (!request.IsTradingEnabled)
        {
            checks.Add(RiskCheckOutcome.Fail(
                RiskCheckNames.TradingMode,
                RiskCheckCodes.TradingDisabled,
                "New order admission is disabled platform-wide, typically following a "
                + "reconciliation failure. An operator must re-enable it."));

            return Reject(checks, sizing: null);
        }

        if (request.TradingMode == TradingMode.Backtest)
        {
            // Backtest orders belong to the simulation engine and must never
            // traverse the live trading path.
            checks.Add(RiskCheckOutcome.Fail(
                RiskCheckNames.TradingMode,
                RiskCheckCodes.TradingMode,
                "Backtest mode cannot submit intents through the live trading path.",
                request.TradingMode.ToString()));

            return Reject(checks, sizing: null);
        }

        checks.Add(RiskCheckOutcome.Pass(RiskCheckNames.TradingMode, request.TradingMode.ToString()));

        if (!request.IsExchangeAvailable)
        {
            checks.Add(RiskCheckOutcome.Fail(
                RiskCheckNames.ExchangeAvailability,
                RiskCheckCodes.ExchangeUnavailable,
                "The exchange is unreachable. Submitting now would produce an order with "
                + "an indeterminate outcome."));

            return Reject(checks, sizing: null);
        }

        checks.Add(RiskCheckOutcome.Pass(RiskCheckNames.ExchangeAvailability));

        if (request.MarketDataAge > policy.MarketDataStalenessThreshold)
        {
            checks.Add(RiskCheckOutcome.Fail(
                RiskCheckNames.MarketDataFreshness,
                RiskCheckCodes.MarketDataStale,
                $"Market data for {request.Symbol} is {request.MarketDataAge.TotalSeconds:F1}s old, "
                + $"beyond the {policy.MarketDataStalenessThreshold.TotalSeconds:F0}s threshold. "
                + "A stop computed from stale prices does not bound the real risk.",
                Format(request.MarketDataAge.TotalSeconds),
                Format(policy.MarketDataStalenessThreshold.TotalSeconds)));

            return Reject(checks, sizing: null);
        }

        checks.Add(RiskCheckOutcome.Pass(
            RiskCheckNames.MarketDataFreshness,
            Format(request.MarketDataAge.TotalSeconds),
            Format(policy.MarketDataStalenessThreshold.TotalSeconds)));

        // =====================================================================
        // Group 2 — size-independent checks.
        //
        // From here on every check is recorded and evaluation continues, so a
        // rejection reports every reason at once.
        // =====================================================================

        checks.Add(EvaluateDuplicateOrder(request));
        checks.Add(EvaluateConfidence(request));
        checks.Add(EvaluateVolatility(request));
        checks.Add(EvaluateOpenPositionCount(request));
        checks.Add(EvaluateDailyLoss(request));

        var stopLossCheck = EvaluateStopLoss(request);
        checks.Add(stopLossCheck);
        checks.Add(EvaluateTakeProfit(request));
        checks.Add(EvaluateProtectiveLevelDirection(request));

        // Sizing needs a usable stop. Without one there is no denominator, so
        // the remaining size-dependent checks cannot run at all.
        if (request.StopLoss is null)
        {
            return Reject(checks, sizing: null);
        }

        // =====================================================================
        // Group 3 — deterministic sizing, then the size-dependent checks.
        // =====================================================================

        var sizing = PositionSizer.Calculate(new PositionSizeRequest(
            request.Side,
            request.EntryPrice,
            request.StopLoss.Value,
            request.PortfolioEquity,
            request.AvailableBalance,
            request.CurrentExposure,
            request.CurrentSymbolExposure,
            request.Precision,
            policy));

        checks.Add(EvaluateSizing(sizing, request));

        if (sizing.IsTradeable)
        {
            checks.Add(EvaluateMaxPositionNotional(sizing, request));
            checks.Add(EvaluateAvailableBalance(sizing, request));
            checks.Add(EvaluatePortfolioExposure(sizing, request));
            checks.Add(EvaluateAssetConcentration(sizing, request));
        }

        var failures = checks.Where(c => !c.Passed).ToArray();

        return failures.Length > 0
            ? Reject(checks, sizing)
            : new RiskEvaluationResult(RiskDecision.Approved, checks, [], sizing);
    }

    // -------------------------------------------------------------------------
    // Size-independent checks
    // -------------------------------------------------------------------------

    private static RiskCheckOutcome EvaluateDuplicateOrder(RiskEvaluationRequest request)
        => request.HasDuplicateOpenOrder
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.DuplicateOrder,
                RiskCheckCodes.DuplicateOrder,
                $"An equivalent {request.Side} order for {request.Symbol} is already open. "
                + "Admitting another would double the intended exposure.")
            : RiskCheckOutcome.Pass(RiskCheckNames.DuplicateOrder);

    private static RiskCheckOutcome EvaluateConfidence(RiskEvaluationRequest request)
        => request.Confidence < request.Policy.MinConfidence
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.MinimumConfidence,
                RiskCheckCodes.MinConfidence,
                $"Confidence {request.Confidence} is below the required minimum "
                + $"{request.Policy.MinConfidence}.",
                request.Confidence.ToString(),
                request.Policy.MinConfidence.ToString())
            : RiskCheckOutcome.Pass(
                RiskCheckNames.MinimumConfidence,
                request.Confidence.ToString(),
                request.Policy.MinConfidence.ToString());

    private static RiskCheckOutcome EvaluateVolatility(RiskEvaluationRequest request)
        => request.SymbolVolatility > request.Policy.MaxVolatility
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.MaxVolatility,
                RiskCheckCodes.MaxVolatility,
                $"Observed volatility {request.SymbolVolatility} for {request.Symbol} exceeds the "
                + $"maximum {request.Policy.MaxVolatility}. Stop distances derived from recent "
                + "candles understate risk in these conditions.",
                request.SymbolVolatility.ToString(),
                request.Policy.MaxVolatility.ToString())
            : RiskCheckOutcome.Pass(
                RiskCheckNames.MaxVolatility,
                request.SymbolVolatility.ToString(),
                request.Policy.MaxVolatility.ToString());

    private static RiskCheckOutcome EvaluateOpenPositionCount(RiskEvaluationRequest request)
        => request.OpenPositionCount >= request.Policy.MaxOpenPositions
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.MaxOpenPositions,
                RiskCheckCodes.MaxOpenPositions,
                $"{request.OpenPositionCount} positions are already open, at the limit of "
                + $"{request.Policy.MaxOpenPositions}.",
                request.OpenPositionCount.ToString(CultureInfo.InvariantCulture),
                request.Policy.MaxOpenPositions.ToString(CultureInfo.InvariantCulture))
            : RiskCheckOutcome.Pass(
                RiskCheckNames.MaxOpenPositions,
                request.OpenPositionCount.ToString(CultureInfo.InvariantCulture),
                request.Policy.MaxOpenPositions.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Rejects when the day's loss has reached its limit.
    /// </summary>
    /// <remarks>
    /// Compares against the loss <em>already incurred</em>, not the loss that
    /// would result from this trade failing. Once the daily budget is spent the
    /// platform stops for the day: the purpose of a daily limit is to end a bad
    /// session, and sizing "one more trade that just fits" defeats it.
    /// </remarks>
    private static RiskCheckOutcome EvaluateDailyLoss(RiskEvaluationRequest request)
    {
        // Positive P&L cannot breach a loss limit.
        if (!request.DailyPnl.IsNegative)
        {
            return RiskCheckOutcome.Pass(RiskCheckNames.MaxDailyLoss, request.DailyPnl.ToString());
        }

        var maxLoss = Money.Create(
            request.Policy.MaxDailyLoss.Of(request.PortfolioEquity.Amount),
            request.PortfolioEquity.Currency);

        var lossSoFar = request.DailyPnl.Abs();

        return lossSoFar >= maxLoss
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.MaxDailyLoss,
                RiskCheckCodes.MaxDailyLoss,
                $"Today's loss of {lossSoFar} has reached the daily limit of {maxLoss} "
                + $"({request.Policy.MaxDailyLoss} of equity). Trading stops until the next UTC day.",
                lossSoFar.ToString(),
                maxLoss.ToString())
            : RiskCheckOutcome.Pass(RiskCheckNames.MaxDailyLoss, lossSoFar.ToString(), maxLoss.ToString());
    }

    private static RiskCheckOutcome EvaluateStopLoss(RiskEvaluationRequest request)
    {
        if (request.StopLoss is not null)
        {
            return RiskCheckOutcome.Pass(RiskCheckNames.StopLossRequirement, request.StopLoss.Value.ToString());
        }

        return request.Policy.RequireStopLoss
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.StopLossRequirement,
                RiskCheckCodes.StopLossRequired,
                "A stop-loss is required. Without one the loss is unbounded and no position "
                + "size can be derived from a risk budget.")
            : RiskCheckOutcome.Fail(
                RiskCheckNames.StopLossRequirement,
                RiskCheckCodes.StopLossRequired,
                "No stop-loss was supplied. Although policy does not mandate one, position "
                + "sizing requires a stop distance, so the intent cannot be sized.");
    }

    private static RiskCheckOutcome EvaluateTakeProfit(RiskEvaluationRequest request)
    {
        if (request.TakeProfit is not null)
        {
            return RiskCheckOutcome.Pass(
                RiskCheckNames.TakeProfitRequirement, request.TakeProfit.Value.ToString());
        }

        return request.Policy.RequireTakeProfit
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.TakeProfitRequirement,
                RiskCheckCodes.TakeProfitRequired,
                "A take-profit target is required by the active risk policy.")
            : RiskCheckOutcome.Pass(RiskCheckNames.TakeProfitRequirement);
    }

    /// <summary>
    /// Verifies the protective levels sit on the correct side of the entry.
    /// </summary>
    /// <remarks>
    /// Catches an inverted stop — a long whose "stop" is above entry. Such an
    /// order is not merely wrong, it is actively dangerous: the stop distance is
    /// still a positive number, so sizing succeeds and produces a position whose
    /// protective order would trigger immediately in profit, leaving the real
    /// downside completely unprotected.
    /// </remarks>
    private static RiskCheckOutcome EvaluateProtectiveLevelDirection(RiskEvaluationRequest request)
    {
        if (request.StopLoss is null)
        {
            return RiskCheckOutcome.Pass(RiskCheckNames.ProtectiveLevelDirection);
        }

        var entry = request.EntryPrice;
        var stop = request.StopLoss.Value;

        var stopOnWrongSide = request.Side == OrderSide.Buy
            ? stop >= entry
            : stop <= entry;

        if (stopOnWrongSide)
        {
            var expected = request.Side == OrderSide.Buy ? "below" : "above";

            return RiskCheckOutcome.Fail(
                RiskCheckNames.ProtectiveLevelDirection,
                RiskCheckCodes.StopLossDirection,
                $"For a {request.Side}, the stop-loss must be {expected} the entry price. "
                + $"Entry {entry}, stop {stop}.",
                stop.ToString(),
                entry.ToString());
        }

        if (request.TakeProfit is not null)
        {
            var target = request.TakeProfit.Value;

            var targetOnWrongSide = request.Side == OrderSide.Buy
                ? target <= entry
                : target >= entry;

            if (targetOnWrongSide)
            {
                var expected = request.Side == OrderSide.Buy ? "above" : "below";

                return RiskCheckOutcome.Fail(
                    RiskCheckNames.ProtectiveLevelDirection,
                    RiskCheckCodes.TakeProfitDirection,
                    $"For a {request.Side}, the take-profit must be {expected} the entry price. "
                    + $"Entry {entry}, target {target}.",
                    target.ToString(),
                    entry.ToString());
            }
        }

        return RiskCheckOutcome.Pass(RiskCheckNames.ProtectiveLevelDirection);
    }

    // -------------------------------------------------------------------------
    // Size-dependent checks
    // -------------------------------------------------------------------------

    private static RiskCheckOutcome EvaluateSizing(
        PositionSizeResult sizing, RiskEvaluationRequest request)
    {
        if (sizing.IsTradeable)
        {
            return RiskCheckOutcome.Pass(
                RiskCheckNames.PositionSizing,
                sizing.Quantity.ToString(),
                sizing.BindingConstraint.ToString());
        }

        var (code, detail) = sizing.BindingConstraint switch
        {
            SizingConstraint.PortfolioExposure => (
                RiskCheckCodes.MaxPortfolioExposure,
                $"No exposure headroom remains: {request.CurrentExposure} is already at the "
                + $"{request.Policy.MaxPortfolioExposure} portfolio limit."),

            SizingConstraint.AssetConcentration => (
                RiskCheckCodes.MaxAssetConcentration,
                $"No concentration headroom remains for {request.Symbol}: "
                + $"{request.CurrentSymbolExposure} is already at the "
                + $"{request.Policy.MaxAssetConcentration} per-asset limit."),

            SizingConstraint.RiskBudget => (
                RiskCheckCodes.RiskBudgetExhausted,
                $"The risk budget of {sizing.RiskBudget} yields no tradeable size. Account "
                + "equity may be zero or the configured risk per trade may be too small."),

            _ => (
                RiskCheckCodes.BelowExchangeMinimum,
                $"The risk-derived size for {request.Symbol} rounds below the exchange minimum "
                + $"(step {request.Precision.StepSize}, minimum notional "
                + $"{request.Precision.MinNotional} {request.Precision.QuoteAsset}). "
                + "Increasing risk per trade or trading a smaller-tick symbol would be required.")
        };

        return RiskCheckOutcome.Fail(
            RiskCheckNames.PositionSizing, code, detail, sizing.Quantity.ToString());
    }

    private static RiskCheckOutcome EvaluateMaxPositionNotional(
        PositionSizeResult sizing, RiskEvaluationRequest request)
        => sizing.Notional > request.Policy.MaxPositionNotional
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.MaxPositionSize,
                RiskCheckCodes.MaxPositionSize,
                $"Order value {sizing.Notional} exceeds the per-position maximum of "
                + $"{request.Policy.MaxPositionNotional}.",
                sizing.Notional.ToString(),
                request.Policy.MaxPositionNotional.ToString())
            : RiskCheckOutcome.Pass(
                RiskCheckNames.MaxPositionSize,
                sizing.Notional.ToString(),
                request.Policy.MaxPositionNotional.ToString());

    private static RiskCheckOutcome EvaluateAvailableBalance(
        PositionSizeResult sizing, RiskEvaluationRequest request)
    {
        // The entry fee is paid from the same balance as the notional, so the
        // comparison must include it.
        var required = sizing.Notional + sizing.EstimatedEntryFee;

        return required > request.AvailableBalance
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.AvailableBalance,
                RiskCheckCodes.InsufficientBalance,
                $"The order needs {required} (including the {sizing.EstimatedEntryFee} entry fee) "
                + $"but only {request.AvailableBalance} is available.",
                required.ToString(),
                request.AvailableBalance.ToString())
            : RiskCheckOutcome.Pass(
                RiskCheckNames.AvailableBalance,
                required.ToString(),
                request.AvailableBalance.ToString());
    }

    private static RiskCheckOutcome EvaluatePortfolioExposure(
        PositionSizeResult sizing, RiskEvaluationRequest request)
    {
        var projected = request.CurrentExposure + sizing.Notional;

        var limit = Money.Create(
            request.Policy.MaxPortfolioExposure.Of(request.PortfolioEquity.Amount),
            request.PortfolioEquity.Currency);

        return projected > limit
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.MaxPortfolioExposure,
                RiskCheckCodes.MaxPortfolioExposure,
                $"Projected exposure {projected} exceeds the limit of {limit} "
                + $"({request.Policy.MaxPortfolioExposure} of equity).",
                projected.ToString(),
                limit.ToString())
            : RiskCheckOutcome.Pass(
                RiskCheckNames.MaxPortfolioExposure, projected.ToString(), limit.ToString());
    }

    private static RiskCheckOutcome EvaluateAssetConcentration(
        PositionSizeResult sizing, RiskEvaluationRequest request)
    {
        var projected = request.CurrentSymbolExposure + sizing.Notional;

        var limit = Money.Create(
            request.Policy.MaxAssetConcentration.Of(request.PortfolioEquity.Amount),
            request.PortfolioEquity.Currency);

        return projected > limit
            ? RiskCheckOutcome.Fail(
                RiskCheckNames.MaxAssetConcentration,
                RiskCheckCodes.MaxAssetConcentration,
                $"Projected {request.Symbol} exposure {projected} exceeds the per-asset limit of "
                + $"{limit} ({request.Policy.MaxAssetConcentration} of equity).",
                projected.ToString(),
                limit.ToString())
            : RiskCheckOutcome.Pass(
                RiskCheckNames.MaxAssetConcentration, projected.ToString(), limit.ToString());
    }

    // -------------------------------------------------------------------------

    private static RiskEvaluationResult Reject(
        List<RiskCheckOutcome> checks, PositionSizeResult? sizing)
    {
        var codes = checks
            .Where(c => !c.Passed)
            .Select(c => c.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new RiskEvaluationResult(RiskDecision.Rejected, checks, codes, sizing);
    }

    private static string Format(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}
