using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Exceptions;

namespace Agentiva.BuildingBlocks.Domain.Primitives;

/// <summary>
/// The exchange-imposed precision and size filters for one trading pair.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the Binance <c>LOT_SIZE</c>, <c>PRICE_FILTER</c> and
/// <c>MIN_NOTIONAL</c> symbol filters, kept exchange-neutral so that other
/// adapters can populate it. An order violating any of these is rejected by the
/// exchange, so normalisation happens before submission, never after.
/// </para>
/// <para>
/// Instances are loaded from the exchange at startup and refreshed periodically;
/// they are never hard-coded, because exchanges change filters without notice.
/// </para>
/// </remarks>
public sealed record InstrumentPrecision
{
    /// <summary>Smallest permitted price increment, e.g. 0.01 for BTCUSDT.</summary>
    public required decimal TickSize { get; init; }

    /// <summary>Smallest permitted quantity increment, e.g. 0.00001 for BTCUSDT.</summary>
    public required decimal StepSize { get; init; }

    /// <summary>Smallest permitted order quantity.</summary>
    public required decimal MinQuantity { get; init; }

    /// <summary>Largest permitted order quantity.</summary>
    public required decimal MaxQuantity { get; init; }

    /// <summary>Smallest permitted order value in quote asset, e.g. 5 USDT.</summary>
    public required decimal MinNotional { get; init; }

    /// <summary>The asset the quantity is denominated in (BTC for BTCUSDT).</summary>
    public required AssetCode BaseAsset { get; init; }

    /// <summary>The asset the price and notional are denominated in (USDT for BTCUSDT).</summary>
    public required AssetCode QuoteAsset { get; init; }

    /// <summary>
    /// Rounds a quantity <em>down</em> to the nearest permitted step.
    /// </summary>
    /// <remarks>
    /// Always downward, never to-nearest. Rounding up could exceed the risk
    /// budget the position size was derived from, or exceed the available
    /// balance and have the exchange reject the order outright. Rounding down
    /// can only ever risk less than intended, which is the safe direction.
    /// </remarks>
    public Quantity NormalizeQuantity(Quantity quantity)
    {
        if (StepSize <= 0m)
        {
            throw new DomainException("domain.precision.invalid_step_size", "Step size must be greater than zero.");
        }

        var steps = decimal.Floor(quantity.Value / StepSize);
        return Quantity.Create(steps * StepSize);
    }

    /// <summary>
    /// Rounds a price to a permitted tick, in the direction that is conservative
    /// for the given side: a buy rounds down, a sell rounds up.
    /// </summary>
    /// <remarks>
    /// Rounding a buy limit down and a sell limit up never improves the price
    /// beyond what the caller asked for. The opposite convention would quietly
    /// pay more per unit than the risk calculation assumed.
    /// </remarks>
    public Price NormalizePrice(Price price, OrderSide side)
    {
        if (TickSize <= 0m)
        {
            throw new DomainException("domain.precision.invalid_tick_size", "Tick size must be greater than zero.");
        }

        var ticks = price.Value / TickSize;
        var rounded = side == OrderSide.Buy
            ? decimal.Floor(ticks) * TickSize
            : decimal.Ceiling(ticks) * TickSize;

        // Flooring a price below one tick would produce zero, which Price rejects.
        // Clamp to a single tick: the smallest price the exchange will accept.
        return Price.Create(rounded <= 0m ? TickSize : rounded);
    }

    /// <summary>
    /// Validates a normalised order against every exchange filter.
    /// </summary>
    /// <returns>
    /// Success, or a failure carrying the specific filter that was violated so
    /// that the rejection reason is auditable rather than a generic error.
    /// </returns>
    public Result ValidateOrder(Price price, Quantity quantity)
    {
        if (quantity.IsZero)
        {
            return Result.Failure(Error.Validation(
                "exchange.filter.quantity_zero",
                "Normalised quantity rounded to zero; the computed position size is below one step."));
        }

        if (quantity.Value < MinQuantity)
        {
            return Result.Failure(Error.Validation(
                "exchange.filter.min_quantity",
                $"Quantity {quantity} is below the exchange minimum of {MinQuantity} {BaseAsset}."));
        }

        if (quantity.Value > MaxQuantity)
        {
            return Result.Failure(Error.Validation(
                "exchange.filter.max_quantity",
                $"Quantity {quantity} exceeds the exchange maximum of {MaxQuantity} {BaseAsset}."));
        }

        // Check the step and tick alignment explicitly rather than trusting the
        // caller to have normalised: a non-aligned value reaching here means a
        // code path skipped normalisation, which is worth surfacing.
        if (StepSize > 0m && quantity.Value % StepSize != 0m)
        {
            return Result.Failure(Error.Validation(
                "exchange.filter.step_size",
                $"Quantity {quantity} is not a multiple of the step size {StepSize}."));
        }

        if (TickSize > 0m && price.Value % TickSize != 0m)
        {
            return Result.Failure(Error.Validation(
                "exchange.filter.tick_size",
                $"Price {price} is not a multiple of the tick size {TickSize}."));
        }

        var notional = Notional(price, quantity);
        return notional.Amount < MinNotional
            ? Result.Failure(Error.Validation(
                "exchange.filter.min_notional",
                $"Order value {notional} is below the exchange minimum of {MinNotional} {QuoteAsset}."))
            : Result.Success();
    }

    /// <summary>The order value in quote asset: price × quantity.</summary>
    public Money Notional(Price price, Quantity quantity)
        => Money.Create(price.Value * quantity.Value, QuoteAsset);
}
