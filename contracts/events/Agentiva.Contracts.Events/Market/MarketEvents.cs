namespace Agentiva.Contracts.Events.Market;

/// <summary>A normalised best-bid/ask tick for one symbol.</summary>
public sealed record MarketTickCreated : IntegrationEvent
{
    public override string EventType => EventTypes.Market.TickCreated;

    public required string Symbol { get; init; }

    /// <summary>Best bid price, in quote asset. Serialised as a JSON string to preserve precision.</summary>
    public required decimal BidPrice { get; init; }

    public required decimal BidQuantity { get; init; }

    public required decimal AskPrice { get; init; }

    public required decimal AskQuantity { get; init; }

    /// <summary>Last traded price.</summary>
    public required decimal LastPrice { get; init; }

    /// <summary>Exchange-assigned event time, used to detect staleness and clock skew.</summary>
    public required DateTimeOffset ExchangeTimestamp { get; init; }

    /// <summary>Which exchange produced this tick, e.g. <c>binance</c>.</summary>
    public required string Exchange { get; init; }
}

/// <summary>A single executed public trade observed on the exchange tape.</summary>
public sealed record MarketTradeCreated : IntegrationEvent
{
    public override string EventType => EventTypes.Market.TradeCreated;

    public required string Symbol { get; init; }

    public required string ExchangeTradeId { get; init; }

    public required decimal Price { get; init; }

    public required decimal Quantity { get; init; }

    /// <summary>True when the buyer was the passive side, as reported by the exchange.</summary>
    public required bool BuyerIsMaker { get; init; }

    public required DateTimeOffset ExchangeTimestamp { get; init; }

    public required string Exchange { get; init; }
}

/// <summary>
/// A completed OHLCV candle.
/// </summary>
/// <remarks>
/// Published only when the candle is closed. Emitting an in-progress candle
/// would let a strategy act on a bar that can still change, which is look-ahead
/// bias in live trading and the single easiest way to produce a backtest that
/// cannot be reproduced in production.
/// </remarks>
public sealed record MarketCandleCreated : IntegrationEvent
{
    public override string EventType => EventTypes.Market.CandleCreated;

    public required string Symbol { get; init; }

    /// <summary>Candle interval, e.g. <c>15m</c> or <c>1h</c>.</summary>
    public required string Timeframe { get; init; }

    public required DateTimeOffset OpenTime { get; init; }

    public required DateTimeOffset CloseTime { get; init; }

    public required decimal Open { get; init; }

    public required decimal High { get; init; }

    public required decimal Low { get; init; }

    public required decimal Close { get; init; }

    /// <summary>Base-asset volume traded in the interval.</summary>
    public required decimal Volume { get; init; }

    /// <summary>Quote-asset volume traded in the interval.</summary>
    public required decimal QuoteVolume { get; init; }

    public required int TradeCount { get; init; }

    public required string Exchange { get; init; }
}

/// <summary>A depth snapshot or diff for one symbol.</summary>
public sealed record MarketOrderBookUpdated : IntegrationEvent
{
    public override string EventType => EventTypes.Market.OrderBookUpdated;

    public required string Symbol { get; init; }

    /// <summary>Exchange update sequence number, used to detect a gapped stream.</summary>
    public required long UpdateId { get; init; }

    /// <summary>Bid levels, best first.</summary>
    public required IReadOnlyList<OrderBookLevel> Bids { get; init; }

    /// <summary>Ask levels, best first.</summary>
    public required IReadOnlyList<OrderBookLevel> Asks { get; init; }

    public required DateTimeOffset ExchangeTimestamp { get; init; }

    public required string Exchange { get; init; }
}

/// <summary>One price level in an order book.</summary>
/// <param name="Price">Level price in quote asset.</param>
/// <param name="Quantity">Resting quantity in base asset.</param>
public sealed record OrderBookLevel(decimal Price, decimal Quantity);

/// <summary>
/// Market data for a symbol has aged past its configured staleness threshold.
/// </summary>
/// <remarks>
/// Consumed by the Risk Service, which fails every subsequent check for the
/// symbol, and by the kill switch. Treated as a first-class event rather than a
/// log line because acting on a stale price is a correctness failure, not a
/// monitoring inconvenience.
/// </remarks>
public sealed record MarketDataStale : IntegrationEvent
{
    public override string EventType => EventTypes.Market.DataStale;

    public required string Symbol { get; init; }

    /// <summary>Timestamp of the most recent data received for the symbol.</summary>
    public required DateTimeOffset LastDataAt { get; init; }

    /// <summary>How long the data has been stale.</summary>
    public required TimeSpan StaleFor { get; init; }

    public required TimeSpan Threshold { get; init; }

    public required string Exchange { get; init; }
}
