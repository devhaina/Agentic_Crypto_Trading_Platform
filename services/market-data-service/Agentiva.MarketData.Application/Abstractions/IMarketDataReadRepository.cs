using Agentiva.MarketData.Application.Contracts;

namespace Agentiva.MarketData.Application.Abstractions;

/// <summary>
/// Read access to the TimescaleDB history, satisfied by Infrastructure.
/// </summary>
/// <remarks>
/// Writing is not part of this interface. Ingestion is a technical pipeline —
/// a WebSocket client landing rows in a hypertable — not a use case invoked by
/// a caller, so it is wired up entirely within Infrastructure. Only reads are
/// an application-level concern, because only reads are driven by an external
/// request (the HTTP endpoints, eventually the AI platform's tools).
/// </remarks>
public interface IMarketDataReadRepository
{
    /// <summary>The most recent tick row for a symbol, or <c>null</c> if none has arrived yet.</summary>
    Task<TickerDto?> GetLatestTickAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>The most recent closed candles for a symbol and timeframe, newest first.</summary>
    Task<IReadOnlyList<CandleDto>> GetRecentCandlesAsync(
        string symbol, string timeframe, int limit, CancellationToken cancellationToken);

    /// <summary>The most recent order book snapshot for a symbol, or <c>null</c> if none has arrived yet.</summary>
    Task<OrderBookDto?> GetLatestOrderBookAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>
    /// The most recent indicator snapshot for a symbol and timeframe, or
    /// <c>null</c> if the Strategy Service has not computed one yet.
    /// </summary>
    Task<IndicatorsDto?> GetLatestIndicatorsAsync(string symbol, string timeframe, CancellationToken cancellationToken);
}
