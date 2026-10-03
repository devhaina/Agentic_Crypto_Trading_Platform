using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Domain.Policies;

namespace Agentiva.Risk.Domain.Sizing;

/// <summary>
/// Derives a position size from a bounded risk budget. Pure and deterministic.
/// </summary>
/// <remarks>
/// <para>
/// This is the single place in the platform that decides how large a trade may
/// be. No AI agent, strategy or caller can influence it beyond supplying an
/// entry and a stop: the proposal's own size hint is never read. That is the
/// structural reason an LLM cannot size a position here — it has no input to
/// the function that does.
/// </para>
/// <para>
/// The textbook formula is
/// <c>quantity = (equity × risk%) ÷ |entry − stop|</c>. Used literally it
/// understates risk in three ways, each of which this implementation corrects:
/// </para>
/// <list type="number">
///   <item><description>
///     <b>Fees are ignored.</b> Being stopped out costs the stop distance
///     <em>plus</em> the fee on both legs. On a 0.5% stop with 0.1% taker fees
///     each way, fees are around 40% of the intended risk — so the naive
///     formula risks roughly 1.4× what was asked for. Corrected below by
///     solving for quantity with the fee terms included.
///   </description></item>
///   <item><description>
///     <b>Slippage is ignored.</b> A market entry fills slightly worse than the
///     reference price, and a stop triggered in a fast market fills worse
///     still. Both are modelled adversely, which widens the stop distance and
///     therefore reduces the size.
///   </description></item>
///   <item><description>
///     <b>Exchange rounding is ignored.</b> Rounding to the step size is always
///     downward, so normalisation can only reduce risk, never increase it.
///   </description></item>
/// </list>
/// <para>
/// Every cap is applied as a <em>minimum</em> against the risk-derived size, and
/// the binding one is reported. Caps never raise a size.
/// </para>
/// </remarks>
public static class PositionSizer
{
    /// <summary>
    /// Calculates the largest position that satisfies every constraint.
    /// </summary>
    /// <param name="request">Sizing inputs.</param>
    /// <returns>The size and the constraint that bound it.</returns>
    public static PositionSizeResult Calculate(PositionSizeRequest request)
    {
        var quote = request.Precision.QuoteAsset;
        var policy = request.Policy;

        // --- 1. Model the fill adversely -------------------------------------
        // A buy is assumed to fill above the reference price and its stop to
        // fill below it; a sell the reverse. Both move against the trader, which
        // is the only safe direction for a risk calculation to assume.
        var effectiveEntry = request.EntryPrice.WithAdverseSlippage(
            policy.SlippageAssumption, request.Side);

        var stopExitSide = request.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
        var effectiveStop = request.StopLoss.WithAdverseSlippage(
            policy.SlippageAssumption, stopExitSide);

        var stopDistance = effectiveEntry.DistanceTo(effectiveStop);

        // A stop at (or through) the entry gives no risk denominator. Treated as
        // untradeable rather than as an unbounded position size.
        if (stopDistance <= 0m)
        {
            return Untradeable(
                effectiveEntry, effectiveStop, quote,
                Money.Zero(quote), SizingConstraint.BelowExchangeMinimum);
        }

        // --- 2. The risk budget ------------------------------------------------
        var riskBudget = Money.Create(
            policy.MaxRiskPerTrade.Of(request.PortfolioEquity.Amount), quote);

        if (riskBudget.Amount <= 0m)
        {
            return Untradeable(
                effectiveEntry, effectiveStop, quote, riskBudget, SizingConstraint.RiskBudget);
        }

        // --- 3. Solve for quantity, fees included --------------------------------
        //
        //   loss_if_stopped(q) = q·stopDistance            (price move)
        //                      + q·effectiveEntry·feeRate  (entry fee)
        //                      + q·effectiveStop·feeRate   (exit fee)
        //
        // Setting loss_if_stopped(q) = riskBudget and solving:
        //
        //   q = riskBudget ÷ (stopDistance + feeRate·(effectiveEntry + effectiveStop))
        //
        // The fee terms in the denominator are what keep actual risk at the
        // budget rather than above it.
        var feeRate = policy.TakerFee.AsFraction;
        var riskPerUnit = stopDistance + (feeRate * (effectiveEntry.Value + effectiveStop.Value));

        var quantityFromRisk = riskBudget.Amount / riskPerUnit;

        var quantity = quantityFromRisk;
        var binding = SizingConstraint.RiskBudget;

        // --- 4. Hard caps, each able only to reduce ------------------------------

        // 4a. Maximum notional per position.
        var maxNotionalQuantity = policy.MaxPositionNotional.Amount / effectiveEntry.Value;
        if (maxNotionalQuantity < quantity)
        {
            quantity = maxNotionalQuantity;
            binding = SizingConstraint.MaxPositionNotional;
        }

        // 4b. Available balance. The entry fee is paid from the same balance, so
        // it belongs in the divisor — sizing to the full balance and then
        // discovering the fee cannot be paid is a guaranteed exchange rejection.
        var affordableQuantity = request.AvailableBalance.Amount / (effectiveEntry.Value * (1m + feeRate));
        if (affordableQuantity < quantity)
        {
            quantity = affordableQuantity;
            binding = SizingConstraint.AvailableBalance;
        }

        // 4c. Remaining portfolio exposure headroom.
        var exposureCap = Money.Create(
            policy.MaxPortfolioExposure.Of(request.PortfolioEquity.Amount), quote);
        var exposureHeadroom = exposureCap.Amount - request.CurrentExposure.Amount;

        if (exposureHeadroom <= 0m)
        {
            return Untradeable(
                effectiveEntry, effectiveStop, quote, riskBudget, SizingConstraint.PortfolioExposure);
        }

        var exposureQuantity = exposureHeadroom / effectiveEntry.Value;
        if (exposureQuantity < quantity)
        {
            quantity = exposureQuantity;
            binding = SizingConstraint.PortfolioExposure;
        }

        // 4d. Remaining per-asset concentration headroom. Stops the portfolio
        // becoming a single-asset bet even while total exposure looks healthy.
        var concentrationCap = Money.Create(
            policy.MaxAssetConcentration.Of(request.PortfolioEquity.Amount), quote);
        var concentrationHeadroom = concentrationCap.Amount - request.CurrentSymbolExposure.Amount;

        if (concentrationHeadroom <= 0m)
        {
            return Untradeable(
                effectiveEntry, effectiveStop, quote, riskBudget, SizingConstraint.AssetConcentration);
        }

        var concentrationQuantity = concentrationHeadroom / effectiveEntry.Value;
        if (concentrationQuantity < quantity)
        {
            quantity = concentrationQuantity;
            binding = SizingConstraint.AssetConcentration;
        }

        // --- 5. Exchange precision -----------------------------------------------
        // Floored to the step size, so this step can only reduce the position.
        var normalised = request.Precision.NormalizeQuantity(Quantity.Create(Math.Max(quantity, 0m)));

        // --- 6. Exchange filters -------------------------------------------------
        var filterResult = request.Precision.ValidateOrder(effectiveEntry, normalised);

        if (normalised.IsZero || filterResult.IsFailure)
        {
            return Untradeable(
                effectiveEntry, effectiveStop, quote, riskBudget, SizingConstraint.BelowExchangeMinimum);
        }

        // --- 7. Report the actual numbers ----------------------------------------
        // Recomputed from the final, normalised quantity rather than carried
        // forward from the ideal figure, so the reported risk is the risk the
        // order actually carries.
        var notional = Money.Create(normalised.Value * effectiveEntry.Value, quote);
        var entryFee = Money.Create(normalised.Value * effectiveEntry.Value * feeRate, quote);
        var exitFee = Money.Create(normalised.Value * effectiveStop.Value * feeRate, quote);
        var actualRisk = Money.Create(
            (normalised.Value * stopDistance) + entryFee.Amount + exitFee.Amount, quote);

        return new PositionSizeResult(
            normalised,
            effectiveEntry,
            effectiveStop,
            notional,
            actualRisk,
            riskBudget,
            entryFee,
            exitFee,
            binding,
            IsTradeable: true);
    }

    private static PositionSizeResult Untradeable(
        Price effectiveEntry,
        Price effectiveStop,
        AssetCode quote,
        Money riskBudget,
        SizingConstraint constraint)
        => new(
            Quantity.Zero,
            effectiveEntry,
            effectiveStop,
            Money.Zero(quote),
            Money.Zero(quote),
            riskBudget,
            Money.Zero(quote),
            Money.Zero(quote),
            constraint,
            IsTradeable: false);
}

/// <summary>Inputs to a position sizing calculation.</summary>
/// <param name="Side">Trade direction, which decides the slippage sign.</param>
/// <param name="EntryPrice">Reference entry price, before slippage.</param>
/// <param name="StopLoss">Protective stop, before slippage.</param>
/// <param name="PortfolioEquity">Total account value, the base for percentage limits.</param>
/// <param name="AvailableBalance">Unencumbered balance in the quote asset.</param>
/// <param name="CurrentExposure">Notional of all open positions.</param>
/// <param name="CurrentSymbolExposure">Notional already open in this symbol.</param>
/// <param name="Precision">Exchange filters for the symbol.</param>
/// <param name="Policy">The risk policy in force.</param>
public sealed record PositionSizeRequest(
    OrderSide Side,
    Price EntryPrice,
    Price StopLoss,
    Money PortfolioEquity,
    Money AvailableBalance,
    Money CurrentExposure,
    Money CurrentSymbolExposure,
    InstrumentPrecision Precision,
    RiskPolicy Policy);
