using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.MarketData.Domain.ValueObjects;

namespace Agentiva.MarketData.Domain.Entities;

/// <summary>A partial order-book depth snapshot for one symbol.</summary>
/// <remarks>
/// Binance's partial-depth stream carries no event timestamp, so
/// <see cref="ExchangeTimestamp"/> is the time this service observed the
/// update. Levels are ordered best-first on each side; <see cref="Create"/>
/// trusts the exchange's ordering rather than re-sorting, since the exchange
/// is the authority on what "best" means for its own book.
/// </remarks>
public sealed record OrderBookSnapshot
{
    private OrderBookSnapshot(
        Symbol symbol,
        long updateId,
        IReadOnlyList<PriceLevel> bids,
        IReadOnlyList<PriceLevel> asks,
        DateTimeOffset exchangeTimestamp,
        string exchange)
    {
        Symbol = symbol;
        UpdateId = updateId;
        Bids = bids;
        Asks = asks;
        ExchangeTimestamp = exchangeTimestamp;
        Exchange = exchange;
    }

    public Symbol Symbol { get; }

    public long UpdateId { get; }

    public IReadOnlyList<PriceLevel> Bids { get; }

    public IReadOnlyList<PriceLevel> Asks { get; }

    public DateTimeOffset ExchangeTimestamp { get; }

    public string Exchange { get; }

    /// <exception cref="DomainException">A level price is not strictly positive.</exception>
    public static OrderBookSnapshot Create(
        string symbol,
        long updateId,
        IReadOnlyList<(decimal Price, decimal Quantity)> bids,
        IReadOnlyList<(decimal Price, decimal Quantity)> asks,
        DateTimeOffset exchangeTimestamp,
        string exchange)
    {
        var symbolValue = Symbol.Create(symbol);

        return new OrderBookSnapshot(
            symbolValue,
            updateId,
            ToLevels(bids),
            ToLevels(asks),
            exchangeTimestamp,
            exchange);
    }

    private static IReadOnlyList<PriceLevel> ToLevels(IReadOnlyList<(decimal Price, decimal Quantity)> levels)
    {
        var result = new PriceLevel[levels.Count];

        for (var i = 0; i < levels.Count; i++)
        {
            result[i] = new PriceLevel(Price.Create(levels[i].Price), levels[i].Quantity);
        }

        return result;
    }
}
