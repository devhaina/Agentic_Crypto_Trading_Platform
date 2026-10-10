using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Portfolio.Domain.Trades;

/// <summary>
/// An immutable record of one completed round trip — the queryable history
/// behind a <see cref="Positions.TradeCompletedDomainEvent"/>.
/// </summary>
/// <remarks>
/// The outbox is a delivery mechanism, not a store: once a row is published
/// it is no longer something daily or lifetime P&amp;L can be summed over.
/// This table is that queryable record, created in the same unit of work as
/// the <see cref="Positions.Position"/> that closed — see
/// <c>OrderFilledHandler</c>.
/// </remarks>
public sealed class Trade : Entity<TradeId>
{
    private Trade()
    {
        // EF Core materialisation.
    }

    private Trade(TradeId id)
        : base(id)
    {
    }

    public TradingAccountId TradingAccountId { get; private set; }

    public Symbol Symbol { get; private set; }

    /// <summary>The side that opened the round trip.</summary>
    public OrderSide Side { get; private set; }

    public Price EntryPrice { get; private set; }

    public Price ExitPrice { get; private set; }

    public Quantity Quantity { get; private set; }

    public decimal RealizedPnl { get; private set; }

    public decimal TotalFees { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset ClosedAt { get; private set; }

    public string QuoteAsset { get; private set; } = string.Empty;

    /// <summary>Builds the queryable record from the domain event a closing fill already raised.</summary>
    public static Trade FromCompletedRoundTrip(
        TradeId id,
        TradingAccountId tradingAccountId,
        Symbol symbol,
        OrderSide side,
        Price entryPrice,
        Price exitPrice,
        Quantity quantity,
        decimal realizedPnl,
        decimal totalFees,
        DateTimeOffset openedAt,
        DateTimeOffset closedAt,
        string quoteAsset)
        => new(id)
        {
            TradingAccountId = tradingAccountId,
            Symbol = symbol,
            Side = side,
            EntryPrice = entryPrice,
            ExitPrice = exitPrice,
            Quantity = quantity,
            RealizedPnl = realizedPnl,
            TotalFees = totalFees,
            OpenedAt = openedAt,
            ClosedAt = closedAt,
            QuoteAsset = quoteAsset
        };
}
