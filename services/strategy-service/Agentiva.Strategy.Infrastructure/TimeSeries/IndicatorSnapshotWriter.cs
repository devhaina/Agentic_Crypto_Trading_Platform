using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.TradingRules.Indicators;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Agentiva.Strategy.Infrastructure.TimeSeries;

/// <summary>
/// Writes computed indicator values to the <c>indicator_snapshots</c>
/// hypertable in the shared <c>market_db</c>.
/// </summary>
/// <remarks>
/// Raw Npgsql, not EF Core — the same reasoning as the Market Data Service's
/// <c>TimescaleMarketDataRepository</c>: EF cannot express a hypertable. The
/// table itself lives in <c>market_db</c>, not this service's own
/// <c>strategy_db</c>, which is intentional rather than a layering slip: that
/// database is the platform's shared time-series store (the schema file
/// documents <c>instrument_precision</c> being read directly by the Risk and
/// Execution services for the same reason), and table-level ownership — the
/// Strategy Service is the only writer of this one table — is the boundary
/// that actually matters, not which service's connection string happens to
/// point at the database.
/// </remarks>
public sealed class IndicatorSnapshotWriter(NpgsqlDataSource dataSource, ILogger<IndicatorSnapshotWriter> logger)
{
    public async Task WriteAsync(
        string symbol,
        string timeframe,
        IndicatorSet indicators,
        string engineVersion,
        DateTimeOffset computedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO market.indicator_snapshots (time, symbol, timeframe, indicators, strategy_version, computed_at)
            VALUES (@time, @symbol, @timeframe, @indicators::jsonb, @strategyVersion, @computedAt)
            ON CONFLICT (symbol, timeframe, time) DO UPDATE SET
                indicators = EXCLUDED.indicators, strategy_version = EXCLUDED.strategy_version,
                computed_at = EXCLUDED.computed_at
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("time", computedAt);
        command.Parameters.AddWithValue("symbol", symbol);
        command.Parameters.AddWithValue("timeframe", timeframe);
        command.Parameters.AddWithValue(
            "indicators", System.Text.Json.JsonSerializer.Serialize(indicators.ToDictionary(), AgentivaJson.Options));
        command.Parameters.AddWithValue("strategyVersion", engineVersion);
        command.Parameters.AddWithValue("computedAt", computedAt);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged and swallowed: a failed indicator write must not stop a
            // strategy from still being evaluated against the values already
            // computed in memory, and must not kill the event consumer.
            logger.LogError(ex, "Failed to persist an indicator snapshot for {Symbol} {Timeframe}.", symbol, timeframe);
        }
    }
}
