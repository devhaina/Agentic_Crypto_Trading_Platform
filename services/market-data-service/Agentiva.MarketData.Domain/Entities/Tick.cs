using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.MarketData.Domain.Entities;

/// <summary>A normalised bid/ask/last quote for one symbol.</summary>
/// <remarks>
/// Sourced from Binance's rolling 24hr ticker stream rather than the lighter
/// book-ticker stream: the book ticker carries no last-traded price and no
/// event timestamp, and both are required here — a last price with no
/// timestamp cannot be checked for staleness.
/// </remarks>
public sealed record Tick
{
    private Tick(
        Symbol symbol,
        Price bidPrice,
        decimal bidQuantity,
        Price askPrice,
        decimal askQuantity,
        Price lastPrice,
        DateTimeOffset exchangeTimestamp,
        string exchange)
    {
        Symbol = symbol;
        BidPrice = bidPrice;
        BidQuantity = bidQuantity;
        AskPrice = askPrice;
        AskQuantity = askQuantity;
        LastPrice = lastPrice;
        ExchangeTimestamp = exchangeTimestamp;
        Exchange = exchange;
    }

    public Symbol Symbol { get; }

    public Price BidPrice { get; }

    /// <summary>
    /// Resting quantity at the best bid. Not <see cref="Quantity"/>: a
    /// momentarily empty book side is valid input, and <c>Quantity.Create</c>
    /// only rejects negative values, not zero — this is kept as a raw decimal
    /// purely so a reader does not assume it is guaranteed positive.
    /// </summary>
    public decimal BidQuantity { get; }

    public Price AskPrice { get; }

    public decimal AskQuantity { get; }

    public Price LastPrice { get; }

    public DateTimeOffset ExchangeTimestamp { get; }

    public string Exchange { get; }

    /// <summary>Builds a tick from already-parsed exchange values.</summary>
    /// <exception cref="Agentiva.BuildingBlocks.Domain.Exceptions.DomainException">
    /// A price is not strictly positive or a quantity is negative.
    /// </exception>
    public static Tick Create(
        string symbol,
        decimal bidPrice,
        decimal bidQuantity,
        decimal askPrice,
        decimal askQuantity,
        decimal lastPrice,
        DateTimeOffset exchangeTimestamp,
        string exchange)
        => new(
            Symbol.Create(symbol),
            Price.Create(bidPrice),
            bidQuantity,
            Price.Create(askPrice),
            askQuantity,
            Price.Create(lastPrice),
            exchangeTimestamp,
            exchange);
}
