using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Portfolio.Domain.Positions;

/// <summary>
/// One symbol's net holding for one trading account, built entirely from
/// order fills.
/// </summary>
/// <remarks>
/// <para>
/// Average-cost accounting, not FIFO lots: every fill that extends the
/// current side re-derives a single weighted-average entry price, and every
/// fill that reduces it realises P&amp;L against that one average rather than
/// against individual historical fills. This is what the published
/// <c>AverageEntryPrice</c> field promises, and it is the same simplification
/// most retail exchanges themselves report P&amp;L against.
/// </para>
/// <para>
/// A position is long-only in practice today — the platform has no margin or
/// borrowing, so a <c>Sell</c> can only ever reduce a <c>Buy</c>-opened
/// position — but the arithmetic here is direction-generic rather than
/// assuming that, so it does not silently misbehave if that ever changes.
/// </para>
/// </remarks>
public sealed class Position : AggregateRoot<PositionId>
{
    private Position()
    {
        // EF Core materialisation.
    }

    private Position(PositionId id)
        : base(id)
    {
    }

    public TradingAccountId TradingAccountId { get; private set; }

    public Symbol Symbol { get; private set; }

    public PositionDirection Direction { get; private set; } = PositionDirection.Flat;

    /// <summary>Current open size. Zero when <see cref="Direction"/> is <see cref="PositionDirection.Flat"/>.</summary>
    public Quantity Quantity { get; private set; } = Quantity.Zero;

    /// <summary>Weighted average entry price across every fill that opened or extended the current side. Null when flat.</summary>
    public Price? AverageEntryPrice { get; private set; }

    /// <summary>Lifetime realised P&amp;L for this symbol, net of fees paid in the quote asset.</summary>
    public decimal RealizedPnl { get; private set; }

    public string QuoteAsset { get; private set; } = string.Empty;

    /// <summary>When the current round trip opened. Null when flat.</summary>
    public DateTimeOffset? OpenedAt { get; private set; }

    /// <summary>The side that opened the current round trip. Null when flat.</summary>
    public OrderSide? OpeningSide { get; private set; }

    /// <summary>Realised P&amp;L accumulated so far in the current, still-open round trip.</summary>
    public decimal RoundTripRealizedPnl { get; private set; }

    /// <summary>Fees (in the quote asset) accumulated so far in the current round trip, entry and exit alike.</summary>
    public decimal RoundTripFees { get; private set; }

    /// <summary>Quantity closed so far in the current round trip, for the weighted exit price.</summary>
    public decimal ClosingQuantityAccumulator { get; private set; }

    /// <summary>Price × quantity closed so far in the current round trip, for the weighted exit price.</summary>
    public decimal ClosingNotionalAccumulator { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Position Create(TradingAccountId tradingAccountId, Symbol symbol, string quoteAsset, DateTimeOffset now)
        => new(PositionId.New())
        {
            TradingAccountId = tradingAccountId,
            Symbol = symbol,
            QuoteAsset = quoteAsset,
            CreatedAt = now,
            UpdatedAt = now
        };

    /// <summary>
    /// Applies one fill: extends the position, reduces it, closes it, or
    /// closes it and flips to the opposite side in one motion. Raises
    /// <see cref="PositionUpdatedDomainEvent"/> always, and
    /// <see cref="TradeCompletedDomainEvent"/> whenever the round trip fully
    /// closes (including as part of a flip).
    /// </summary>
    /// <param name="side">Trade direction of the fill.</param>
    /// <param name="fillQuantity">Base-asset quantity this fill transacted.</param>
    /// <param name="fillPrice">
    /// This fill's own price. Also what the published event's unrealised
    /// figure is marked against, which is what makes that figure
    /// reproducible from the event alone — the read side marks against a
    /// live price instead; see <c>GetPositionsQueryHandler</c>.
    /// </param>
    /// <param name="feePaid">Fee charged on this fill.</param>
    /// <param name="feeAsset">Asset the fee was charged in.</param>
    /// <param name="quoteAsset">Quote asset this position is denominated in.</param>
    /// <param name="filledAt">When the fill occurred.</param>
    /// <param name="now">Current instant, for the aggregate's own timestamps.</param>
    public void ApplyFill(
        OrderSide side,
        Quantity fillQuantity,
        Price fillPrice,
        decimal feePaid,
        string feeAsset,
        string quoteAsset,
        DateTimeOffset filledAt,
        DateTimeOffset now)
    {
        QuoteAsset = quoteAsset;

        // Fees in an asset other than the quote asset (a BNB-discounted fee,
        // for instance) are not converted and so are not reflected in
        // RealizedPnl — see docs/architecture/known-limitations.md.
        var feeInQuote = string.Equals(feeAsset, quoteAsset, StringComparison.OrdinalIgnoreCase) ? feePaid : 0m;

        var signedQuantity = Direction switch
        {
            PositionDirection.Long => Quantity.Value,
            PositionDirection.Short => -Quantity.Value,
            _ => 0m
        };

        var delta = side == OrderSide.Buy ? fillQuantity.Value : -fillQuantity.Value;
        var isExtending = signedQuantity == 0m || Math.Sign(delta) == Math.Sign(signedQuantity);

        if (isExtending)
        {
            ApplyExtendingFill(side, fillQuantity, fillPrice, feeInQuote, filledAt);
        }
        else
        {
            ApplyReducingFill(side, fillQuantity, fillPrice, feeInQuote, filledAt, now);
        }

        UpdatedAt = now;
        RaisePositionUpdated(fillPrice.Value, now);
    }

    private void ApplyExtendingFill(
        OrderSide side, Quantity fillQuantity, Price fillPrice, decimal feeInQuote, DateTimeOffset filledAt)
    {
        if (OpenedAt is null)
        {
            OpenedAt = filledAt;
            OpeningSide = side;
        }

        var oldQuantity = Quantity.Value;
        var oldAverage = AverageEntryPrice?.Value ?? 0m;
        var newQuantity = oldQuantity + fillQuantity.Value;

        AverageEntryPrice = Price.Create(((oldAverage * oldQuantity) + (fillPrice.Value * fillQuantity.Value)) / newQuantity);
        Quantity = Quantity.Create(newQuantity);
        Direction = side == OrderSide.Buy ? PositionDirection.Long : PositionDirection.Short;
        RoundTripFees += feeInQuote;
    }

    private void ApplyReducingFill(
        OrderSide side, Quantity fillQuantity, Price fillPrice, decimal feeInQuote, DateTimeOffset filledAt,
        DateTimeOffset now)
    {
        var closingAmount = Math.Min(fillQuantity.Value, Quantity.Value);
        var overflow = fillQuantity.Value - closingAmount;

        var entryPrice = AverageEntryPrice!.Value.Value;

        var pnlPerUnit = Direction == PositionDirection.Long
            ? fillPrice.Value - entryPrice
            : entryPrice - fillPrice.Value;

        var realizedThisFill = pnlPerUnit * closingAmount;
        RealizedPnl += realizedThisFill;
        RoundTripRealizedPnl += realizedThisFill;

        ClosingQuantityAccumulator += closingAmount;
        ClosingNotionalAccumulator += fillPrice.Value * closingAmount;

        var remainingQuantity = Quantity.Value - closingAmount;

        if (remainingQuantity > 0m)
        {
            // Partial close. The average entry price of the remaining open
            // quantity is unchanged — average-cost accounting realises P&L
            // on the closed slice only, and leaves the rest at its original basis.
            RoundTripFees += feeInQuote;
            Quantity = Quantity.Create(remainingQuantity);
            return;
        }

        // Fully closed. Attribute this fill's fee to the round trip that is
        // closing before resetting the accumulators for it.
        RoundTripFees += overflow > 0m ? feeInQuote * (closingAmount / fillQuantity.Value) : feeInQuote;
        RaiseTradeCompleted(now);

        if (overflow > 0m)
        {
            // The fill overshot the existing side and flips straight into a
            // new round trip on the opposite side with the remainder.
            Direction = side == OrderSide.Buy ? PositionDirection.Long : PositionDirection.Short;
            Quantity = Quantity.Create(overflow);
            AverageEntryPrice = fillPrice;
            OpenedAt = filledAt;
            OpeningSide = side;
            RoundTripFees = feeInQuote * (overflow / fillQuantity.Value);
        }
        else
        {
            Direction = PositionDirection.Flat;
            Quantity = Quantity.Zero;
            AverageEntryPrice = null;
        }
    }

    private void RaiseTradeCompleted(DateTimeOffset now)
    {
        var exitPrice = ClosingQuantityAccumulator > 0m
            ? ClosingNotionalAccumulator / ClosingQuantityAccumulator
            : 0m;

        Raise(new TradeCompletedDomainEvent(
            now,
            TradeId.New().Value,
            TradingAccountId.Value,
            Symbol.Value,
            (OpeningSide ?? OrderSide.Buy).ToString().ToUpperInvariant(),
            AverageEntryPrice?.Value ?? 0m,
            exitPrice,
            ClosingQuantityAccumulator,
            RoundTripRealizedPnl,
            RoundTripFees,
            OpenedAt ?? now,
            now,
            QuoteAsset));

        RoundTripRealizedPnl = 0m;
        RoundTripFees = 0m;
        ClosingQuantityAccumulator = 0m;
        ClosingNotionalAccumulator = 0m;
        OpenedAt = null;
        OpeningSide = null;
    }

    private void RaisePositionUpdated(decimal markPrice, DateTimeOffset now)
    {
        var unrealizedPnl = Direction == PositionDirection.Flat
            ? 0m
            : Direction == PositionDirection.Long
                ? (markPrice - AverageEntryPrice!.Value.Value) * Quantity.Value
                : (AverageEntryPrice!.Value.Value - markPrice) * Quantity.Value;

        Raise(new PositionUpdatedDomainEvent(
            now,
            Id.Value,
            TradingAccountId.Value,
            Symbol.Value,
            Direction.ToString().ToUpperInvariant(),
            Quantity.Value,
            AverageEntryPrice?.Value ?? 0m,
            RealizedPnl,
            unrealizedPnl,
            markPrice,
            QuoteAsset));
    }
}
