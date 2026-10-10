using Microsoft.Extensions.Logging;
using Npgsql;

namespace Agentiva.Portfolio.Infrastructure.TimeSeries;

/// <summary>
/// Writes one point-in-time row to the <c>portfolio_snapshots</c> hypertable
/// in the shared <c>market_db</c>.
/// </summary>
/// <remarks>
/// Raw Npgsql, not EF Core — the same reasoning as the Strategy Service's
/// <c>IndicatorSnapshotWriter</c>: EF cannot express a hypertable, and the
/// table lives in <c>market_db</c> on purpose (the platform's shared
/// time-series store) even though this service is its only writer. Called
/// once per processed fill, after the position and account aggregates have
/// already been saved, so a failed write here never loses the fill itself —
/// it only loses one data point on the equity curve.
/// </remarks>
public sealed class PortfolioSnapshotWriter(NpgsqlDataSource dataSource, ILogger<PortfolioSnapshotWriter> logger)
{
    public async Task WriteAsync(
        Guid tradingAccountId,
        decimal totalValue,
        decimal availableBalance,
        decimal totalExposure,
        decimal realizedPnl,
        decimal unrealizedPnl,
        int openPositionCount,
        string quoteAsset,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            // The drawdown this row reports is peak-to-trough against every
            // total_value this account has ever recorded, including rows
            // from a prior deployment — there is no separate high-water-mark
            // table to keep in sync, just one more read against the same
            // hypertable this row is about to join.
            var highWaterMark = await ReadHighWaterMarkAsync(tradingAccountId, cancellationToken);
            var peak = Math.Max(highWaterMark ?? totalValue, totalValue);
            var drawdownPercent = peak > 0m ? Math.Max(0m, (peak - totalValue) / peak * 100m) : 0m;

            const string sql = """
                INSERT INTO market.portfolio_snapshots
                    (time, trading_account_id, total_value, available_balance, total_exposure,
                     realized_pnl, unrealized_pnl, open_position_count, drawdown_percent, quote_asset)
                VALUES (@time, @accountId, @totalValue, @availableBalance, @totalExposure,
                        @realizedPnl, @unrealizedPnl, @openPositionCount, @drawdownPercent, @quoteAsset)
                """;

            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("time", now);
            command.Parameters.AddWithValue("accountId", tradingAccountId);
            command.Parameters.AddWithValue("totalValue", totalValue);
            command.Parameters.AddWithValue("availableBalance", availableBalance);
            command.Parameters.AddWithValue("totalExposure", totalExposure);
            command.Parameters.AddWithValue("realizedPnl", realizedPnl);
            command.Parameters.AddWithValue("unrealizedPnl", unrealizedPnl);
            command.Parameters.AddWithValue("openPositionCount", openPositionCount);
            command.Parameters.AddWithValue("drawdownPercent", drawdownPercent);
            command.Parameters.AddWithValue("quoteAsset", quoteAsset);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged and swallowed: a failed snapshot write must not fail
            // the fill it is reporting on, and must not kill the event consumer.
            logger.LogError(ex, "Failed to persist a portfolio snapshot for account {TradingAccountId}.", tradingAccountId);
        }
    }

    private async Task<decimal?> ReadHighWaterMarkAsync(Guid tradingAccountId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT MAX(total_value) FROM market.portfolio_snapshots WHERE trading_account_id = @accountId";

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("accountId", tradingAccountId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToDecimal(result);
    }
}
