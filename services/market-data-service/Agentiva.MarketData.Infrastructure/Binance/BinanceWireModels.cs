using System.Text.Json.Serialization;

namespace Agentiva.MarketData.Infrastructure.Binance;

// =============================================================================
// Shapes of Binance's raw (single-stream) WebSocket payloads.
//
// Property names are Binance's own abbreviated wire keys, not Agentiva's
// naming convention — these types exist purely to deserialise one exchange's
// specific format and are mapped onto domain entities immediately after. Every
// numeric financial field arrives as a JSON string, which System.Text.Json
// binds straight onto a decimal without ever passing through a double.
// =============================================================================

/// <summary>
/// Payload of Binance's rolling 24hr ticker stream (<c>&lt;symbol&gt;@ticker</c>).
/// Only the fields this service uses are mapped; the rest are ignored.
/// </summary>
public sealed record BinanceTickerPayload(
    [property: JsonPropertyName("E")] long EventTimeMs,
    [property: JsonPropertyName("s")] string Symbol,
    [property: JsonPropertyName("c")] decimal LastPrice,
    [property: JsonPropertyName("b")] decimal BidPrice,
    [property: JsonPropertyName("B")] decimal BidQuantity,
    [property: JsonPropertyName("a")] decimal AskPrice,
    [property: JsonPropertyName("A")] decimal AskQuantity);

/// <summary>Payload of Binance's raw trade stream (<c>&lt;symbol&gt;@trade</c>).</summary>
public sealed record BinanceTradePayload(
    [property: JsonPropertyName("T")] long TradeTimeMs,
    [property: JsonPropertyName("s")] string Symbol,
    [property: JsonPropertyName("t")] long TradeId,
    [property: JsonPropertyName("p")] decimal Price,
    [property: JsonPropertyName("q")] decimal Quantity,
    [property: JsonPropertyName("m")] bool IsBuyerMaker);

/// <summary>Payload of Binance's kline/candlestick stream (<c>&lt;symbol&gt;@kline_&lt;interval&gt;</c>).</summary>
public sealed record BinanceKlineEnvelope(
    [property: JsonPropertyName("s")] string Symbol,
    [property: JsonPropertyName("k")] BinanceKlinePayload Kline);

/// <summary>
/// The nested <c>k</c> object of a kline event. <c>IsClosed</c> is whether this
/// bar is final; a false value is dropped by the handler, never persisted.
/// </summary>
public sealed record BinanceKlinePayload(
    [property: JsonPropertyName("t")] long OpenTimeMs,
    [property: JsonPropertyName("T")] long CloseTimeMs,
    [property: JsonPropertyName("i")] string Interval,
    [property: JsonPropertyName("o")] decimal Open,
    [property: JsonPropertyName("h")] decimal High,
    [property: JsonPropertyName("l")] decimal Low,
    [property: JsonPropertyName("c")] decimal Close,
    [property: JsonPropertyName("v")] decimal Volume,
    [property: JsonPropertyName("q")] decimal QuoteVolume,
    [property: JsonPropertyName("n")] int TradeCount,
    [property: JsonPropertyName("x")] bool IsClosed);

/// <summary>
/// Payload of Binance's partial book depth stream
/// (<c>&lt;symbol&gt;@depth&lt;levels&gt;@&lt;interval&gt;</c>).
/// </summary>
/// <remarks>
/// Carries neither a symbol nor an event time. The symbol is instead known
/// from which single-stream connection delivered the message — see
/// <see cref="BinanceStreamConnection{TPayload}"/> — and the timestamp is the
/// time this service observed the update.
/// </remarks>
public sealed record BinanceDepthPayload(
    [property: JsonPropertyName("lastUpdateId")] long LastUpdateId,
    [property: JsonPropertyName("bids")] List<string[]> Bids,
    [property: JsonPropertyName("asks")] List<string[]> Asks);
