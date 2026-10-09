namespace Agentiva.MarketData.Application.Contracts;

/// <summary>
/// One price level, as exposed over the cache and the API.
/// </summary>
/// <remarks>
/// Deliberately plain primitives rather than the domain's <c>PriceLevel</c>:
/// every type that crosses a serialisation boundary in this platform does, so
/// that a value object's constructor shape never becomes System.Text.Json's
/// problem. See the published event contracts for the same convention.
/// </remarks>
public sealed record PriceLevelDto(decimal Price, decimal Quantity);

/// <summary>A bid/ask/last quote, as cached and as returned by the ticker endpoint.</summary>
public sealed record TickerDto(
    string Symbol,
    decimal BidPrice,
    decimal BidQuantity,
    decimal AskPrice,
    decimal AskQuantity,
    decimal LastPrice,
    DateTimeOffset ExchangeTimestamp,
    string Exchange);

/// <summary>A closed OHLCV candle, as cached and as returned by the candles endpoint.</summary>
public sealed record CandleDto(
    string Symbol,
    string Timeframe,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal QuoteVolume,
    int TradeCount,
    string Exchange);

/// <summary>A depth snapshot, as cached and as returned by the order book endpoint.</summary>
public sealed record OrderBookDto(
    string Symbol,
    long UpdateId,
    IReadOnlyList<PriceLevelDto> Bids,
    IReadOnlyList<PriceLevelDto> Asks,
    DateTimeOffset ExchangeTimestamp,
    string Exchange);

/// <summary>
/// The most recent indicator values computed for a symbol and timeframe.
/// </summary>
/// <remarks>
/// Written by the Strategy Service, read here: <c>indicator_snapshots</c> is
/// a shared TimescaleDB hypertable in this service's own <c>market_db</c>,
/// and the Strategy Service is its only writer. This endpoint exists so the
/// AI platform's <c>get_indicators</c> tool — routed by the gateway to this
/// service's <c>/market</c> cluster, not the Strategy Service's — has
/// something to call.
/// </remarks>
public sealed record IndicatorsDto(
    string Symbol,
    string Timeframe,
    IReadOnlyDictionary<string, decimal> Indicators,
    DateTimeOffset ComputedAt);

/// <summary>Live ingestion status for one configured symbol.</summary>
/// <param name="Symbol">The symbol this status describes.</param>
/// <param name="IsConnected">Whether every stream for this symbol currently holds an open connection.</param>
/// <param name="LastMessageAt">When the most recent message of any kind for this symbol arrived.</param>
/// <param name="IsStale">Whether the data age exceeds the configured staleness threshold.</param>
/// <param name="StaleForSeconds">How long past the threshold the data is, zero while fresh.</param>
public sealed record SymbolFeedStatusDto(
    string Symbol,
    bool IsConnected,
    DateTimeOffset? LastMessageAt,
    bool IsStale,
    double StaleForSeconds);
