using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.MarketData.Domain.Entities;

/// <summary>A single executed public trade observed on the exchange tape.</summary>
public sealed record Trade
{
    private Trade(
        Symbol symbol,
        string exchangeTradeId,
        Price price,
        decimal quantity,
        bool buyerIsMaker,
        DateTimeOffset exchangeTimestamp,
        string exchange)
    {
        Symbol = symbol;
        ExchangeTradeId = exchangeTradeId;
        Price = price;
        Quantity = quantity;
        BuyerIsMaker = buyerIsMaker;
        ExchangeTimestamp = exchangeTimestamp;
        Exchange = exchange;
    }

    public Symbol Symbol { get; }

    public string ExchangeTradeId { get; }

    public Price Price { get; }

    public decimal Quantity { get; }

    public bool BuyerIsMaker { get; }

    public DateTimeOffset ExchangeTimestamp { get; }

    public string Exchange { get; }

    /// <exception cref="DomainException">
    /// The trade id is empty, the price is not strictly positive, or the quantity is negative.
    /// </exception>
    public static Trade Create(
        string symbol,
        string exchangeTradeId,
        decimal price,
        decimal quantity,
        bool buyerIsMaker,
        DateTimeOffset exchangeTimestamp,
        string exchange)
    {
        if (string.IsNullOrWhiteSpace(exchangeTradeId))
        {
            throw new DomainException("domain.trade.missing_id", "A trade must carry the exchange's trade id.");
        }

        if (quantity < 0m)
        {
            throw new DomainException(
                "domain.trade.negative_quantity", $"Trade quantity must not be negative but was {quantity}.");
        }

        return new Trade(
            Symbol.Create(symbol), exchangeTradeId, Price.Create(price), quantity, buyerIsMaker,
            exchangeTimestamp, exchange);
    }
}
