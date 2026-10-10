using Agentiva.Backtesting.Domain.Runs;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.TradingRules.Indicators;

namespace Agentiva.Backtesting.Application.Abstractions;

/// <summary>Access to persisted backtest runs.</summary>
public interface IBacktestRunRepository
{
    void Add(BacktestRun run);

    Task<BacktestRun?> GetAsync(BacktestId id, CancellationToken cancellationToken);

    /// <summary>Most recently created runs first.</summary>
    Task<IReadOnlyList<BacktestRun>> ListAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Reads closed historical candles for one symbol, timeframe and period.
/// </summary>
/// <remarks>
/// The only thing this service calls outside its own database: a direct,
/// read-only query against the shared <c>market_db</c>'s
/// <c>market_candles</c> hypertable — the same table-level-ownership
/// convention <c>IndicatorSnapshotWriter</c> (Strategy Service) and
/// <c>PortfolioSnapshotWriter</c> (Portfolio Service) already established,
/// just reading instead of writing. No exchange credential, no write
/// access — this interface cannot create a candle, only replay ones the
/// Market Data Service already wrote.
/// </remarks>
public interface IHistoricalCandleReader
{
    /// <summary>
    /// Returns every closed candle for <paramref name="symbol"/> and
    /// <paramref name="timeframe"/> with an open time in
    /// [<paramref name="periodStart"/>, <paramref name="periodEnd"/>),
    /// oldest first. Empty, never an exception, when nothing was ingested
    /// for that window — the caller decides whether that is a failure.
    /// </summary>
    Task<IReadOnlyList<PriceBar>> GetCandlesAsync(
        string symbol,
        string timeframe,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);
}
