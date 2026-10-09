using Agentiva.MarketData.Application.Contracts;

namespace Agentiva.MarketData.Application.Abstractions;

/// <summary>
/// Redis-backed latest-value cache for market data, satisfied by Infrastructure.
/// </summary>
/// <remarks>
/// Holds only the single latest value per symbol (and per symbol+timeframe for
/// candles) — this is a cache, not a store of record. History lives in
/// TimescaleDB via <see cref="IMarketDataReadRepository"/>; this exists purely
/// so a read of "what is the price right now" does not need a database round
/// trip on every call.
/// </remarks>
public interface IMarketDataCache
{
    Task SetLatestTickerAsync(TickerDto ticker, CancellationToken cancellationToken);

    Task<TickerDto?> GetLatestTickerAsync(string symbol, CancellationToken cancellationToken);

    Task SetLatestCandleAsync(CandleDto candle, CancellationToken cancellationToken);

    Task<CandleDto?> GetLatestCandleAsync(string symbol, string timeframe, CancellationToken cancellationToken);

    Task SetLatestOrderBookAsync(OrderBookDto orderBook, CancellationToken cancellationToken);

    Task<OrderBookDto?> GetLatestOrderBookAsync(string symbol, CancellationToken cancellationToken);
}
