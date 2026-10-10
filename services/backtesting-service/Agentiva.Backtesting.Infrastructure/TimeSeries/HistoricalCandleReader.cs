using Agentiva.Backtesting.Application.Abstractions;
using Agentiva.BuildingBlocks.TradingRules.Indicators;
using Npgsql;

namespace Agentiva.Backtesting.Infrastructure.TimeSeries;

/// <summary>
/// Reads closed candles directly from the <c>market_candles</c> hypertable
/// in the shared <c>market_db</c>.
/// </summary>
/// <remarks>
/// Raw Npgsql, not EF Core — the same reasoning as the Market Data Service's
/// own repository and the Strategy/Portfolio Services'
/// <c>IndicatorSnapshotWriter</c>/<c>PortfolioSnapshotWriter</c>: EF cannot
/// express a hypertable, and table-level ownership (the Market Data Service
/// is the only writer; this is one more read-only consumer) is the boundary
/// that matters, not which service's connection string happens to point at
/// the database. <c>is_closed</c> is filtered to <c>true</c> explicitly —
/// the schema's own comment on that column explains why: an in-progress
/// candle used here would make a backtest irreproducible.
/// </remarks>
public sealed class HistoricalCandleReader(NpgsqlDataSource dataSource) : IHistoricalCandleReader
{
    public async Task<IReadOnlyList<PriceBar>> GetCandlesAsync(
        string symbol, string timeframe, DateTimeOffset periodStart, DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT time, open, high, low, close, volume
            FROM market.market_candles
            WHERE symbol = @symbol
              AND timeframe = @timeframe
              AND is_closed = TRUE
              AND time >= @periodStart
              AND time < @periodEnd
            ORDER BY time ASC
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("symbol", symbol);
        command.Parameters.AddWithValue("timeframe", timeframe);
        command.Parameters.AddWithValue("periodStart", periodStart);
        command.Parameters.AddWithValue("periodEnd", periodEnd);

        var bars = new List<PriceBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            bars.Add(new PriceBar(
                reader.GetFieldValue<DateTimeOffset>(0),
                reader.GetFieldValue<decimal>(1),
                reader.GetFieldValue<decimal>(2),
                reader.GetFieldValue<decimal>(3),
                reader.GetFieldValue<decimal>(4),
                reader.GetFieldValue<decimal>(5)));
        }

        return bars;
    }
}
