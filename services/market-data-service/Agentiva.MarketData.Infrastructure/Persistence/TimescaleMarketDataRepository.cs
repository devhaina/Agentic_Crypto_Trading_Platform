using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;
using Agentiva.MarketData.Domain.Entities;
using Agentiva.MarketData.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Agentiva.MarketData.Infrastructure.Persistence;

/// <summary>
/// Reads and writes the <c>market</c> schema hypertables provisioned by
/// <c>infrastructure/timescale/init/01-market-schema.sql</c>.
/// </summary>
/// <remarks>
/// Raw Npgsql, not Entity Framework. EF has no way to express a hypertable, a
/// compression policy or the <c>create_hypertable</c> call that schema already
/// ran at the database level — the same reasoning the schema file itself
/// documents for provisioning with SQL instead of migrations. Only the
/// read methods are exposed through <see cref="IMarketDataReadRepository"/>:
/// writing is driven entirely by the ingestion pipeline inside this assembly,
/// not by an application use case, so the write methods below are plain public
/// members rather than part of that interface.
/// </remarks>
public sealed class TimescaleMarketDataRepository(NpgsqlDataSource dataSource, ILogger<TimescaleMarketDataRepository> logger)
    : IMarketDataReadRepository
{
    public async Task WriteTickAsync(Tick tick, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO market.market_ticks (time, symbol, exchange, bid_price, bid_quantity, ask_price, ask_quantity, last_price)
            VALUES (@time, @symbol, @exchange, @bidPrice, @bidQuantity, @askPrice, @askQuantity, @lastPrice)
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("time", tick.ExchangeTimestamp);
        command.Parameters.AddWithValue("symbol", tick.Symbol.Value);
        command.Parameters.AddWithValue("exchange", tick.Exchange);
        command.Parameters.AddWithValue("bidPrice", tick.BidPrice.Value);
        command.Parameters.AddWithValue("bidQuantity", tick.BidQuantity);
        command.Parameters.AddWithValue("askPrice", tick.AskPrice.Value);
        command.Parameters.AddWithValue("askQuantity", tick.AskQuantity);
        command.Parameters.AddWithValue("lastPrice", tick.LastPrice.Value);

        await ExecuteAsync(command, "tick", tick.Symbol.Value, cancellationToken);
    }

    public async Task WriteTradeAsync(Trade trade, CancellationToken cancellationToken)
    {
        // ON CONFLICT DO NOTHING against ux_market_trades_exchange_id: a
        // WebSocket reconnect routinely replays the last few trades, and the
        // schema's own unique index exists precisely so that is a no-op
        // rather than a duplicate row.
        const string sql = """
            INSERT INTO market.market_trades (time, symbol, exchange, exchange_trade_id, price, quantity, buyer_is_maker)
            VALUES (@time, @symbol, @exchange, @exchangeTradeId, @price, @quantity, @buyerIsMaker)
            ON CONFLICT (exchange, symbol, exchange_trade_id, time) DO NOTHING
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("time", trade.ExchangeTimestamp);
        command.Parameters.AddWithValue("symbol", trade.Symbol.Value);
        command.Parameters.AddWithValue("exchange", trade.Exchange);
        command.Parameters.AddWithValue("exchangeTradeId", trade.ExchangeTradeId);
        command.Parameters.AddWithValue("price", trade.Price.Value);
        command.Parameters.AddWithValue("quantity", trade.Quantity);
        command.Parameters.AddWithValue("buyerIsMaker", trade.BuyerIsMaker);

        await ExecuteAsync(command, "trade", trade.Symbol.Value, cancellationToken);
    }

    public async Task WriteCandleAsync(Candle candle, CancellationToken cancellationToken)
    {
        // ON CONFLICT ... DO UPDATE against ux_market_candles_key: a replayed
        // close of the same bar (another routine reconnect effect) corrects
        // the row in place instead of producing a second one.
        const string sql = """
            INSERT INTO market.market_candles
                (time, symbol, timeframe, exchange, open, high, low, close, volume, quote_volume, trade_count, close_time, is_closed)
            VALUES
                (@time, @symbol, @timeframe, @exchange, @open, @high, @low, @close, @volume, @quoteVolume, @tradeCount, @closeTime, TRUE)
            ON CONFLICT (symbol, timeframe, time) DO UPDATE SET
                open = EXCLUDED.open, high = EXCLUDED.high, low = EXCLUDED.low, close = EXCLUDED.close,
                volume = EXCLUDED.volume, quote_volume = EXCLUDED.quote_volume,
                trade_count = EXCLUDED.trade_count, close_time = EXCLUDED.close_time
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("time", candle.OpenTime);
        command.Parameters.AddWithValue("symbol", candle.Symbol.Value);
        command.Parameters.AddWithValue("timeframe", candle.Timeframe.Value);
        command.Parameters.AddWithValue("exchange", candle.Exchange);
        command.Parameters.AddWithValue("open", candle.Open.Value);
        command.Parameters.AddWithValue("high", candle.High.Value);
        command.Parameters.AddWithValue("low", candle.Low.Value);
        command.Parameters.AddWithValue("close", candle.Close.Value);
        command.Parameters.AddWithValue("volume", candle.Volume);
        command.Parameters.AddWithValue("quoteVolume", candle.QuoteVolume);
        command.Parameters.AddWithValue("tradeCount", candle.TradeCount);
        command.Parameters.AddWithValue("closeTime", candle.CloseTime);

        await ExecuteAsync(command, "candle", candle.Symbol.Value, cancellationToken);
    }

    public async Task WriteOrderBookAsync(OrderBookSnapshot snapshot, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO market.orderbook_snapshots (time, symbol, exchange, update_id, bids, asks)
            VALUES (@time, @symbol, @exchange, @updateId, @bids::jsonb, @asks::jsonb)
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("time", snapshot.ExchangeTimestamp);
        command.Parameters.AddWithValue("symbol", snapshot.Symbol.Value);
        command.Parameters.AddWithValue("exchange", snapshot.Exchange);
        command.Parameters.AddWithValue("updateId", snapshot.UpdateId);
        command.Parameters.AddWithValue("bids", ToJson(snapshot.Bids));
        command.Parameters.AddWithValue("asks", ToJson(snapshot.Asks));

        await ExecuteAsync(command, "order book snapshot", snapshot.Symbol.Value, cancellationToken);
    }

    public async Task<TickerDto?> GetLatestTickAsync(string symbol, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT time, symbol, exchange, bid_price, bid_quantity, ask_price, ask_quantity, last_price
            FROM market.market_ticks
            WHERE symbol = @symbol
            ORDER BY time DESC
            LIMIT 1
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("symbol", symbol.ToUpperInvariant());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TickerDto(
            Symbol: reader.GetString(1),
            BidPrice: reader.GetFieldValue<decimal>(3),
            BidQuantity: reader.GetFieldValue<decimal>(4),
            AskPrice: reader.GetFieldValue<decimal>(5),
            AskQuantity: reader.GetFieldValue<decimal>(6),
            LastPrice: reader.GetFieldValue<decimal>(7),
            ExchangeTimestamp: reader.GetFieldValue<DateTimeOffset>(0),
            Exchange: reader.GetString(2));
    }

    public async Task<IReadOnlyList<CandleDto>> GetRecentCandlesAsync(
        string symbol, string timeframe, int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT time, close_time, symbol, timeframe, exchange, open, high, low, close, volume, quote_volume, trade_count
            FROM market.market_candles
            WHERE symbol = @symbol AND timeframe = @timeframe AND is_closed = TRUE
            ORDER BY time DESC
            LIMIT @limit
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("symbol", symbol.ToUpperInvariant());
        command.Parameters.AddWithValue("timeframe", timeframe);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));

        var results = new List<CandleDto>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CandleDto(
                Symbol: reader.GetString(2),
                Timeframe: reader.GetString(3),
                OpenTime: reader.GetFieldValue<DateTimeOffset>(0),
                CloseTime: reader.GetFieldValue<DateTimeOffset>(1),
                Open: reader.GetFieldValue<decimal>(5),
                High: reader.GetFieldValue<decimal>(6),
                Low: reader.GetFieldValue<decimal>(7),
                Close: reader.GetFieldValue<decimal>(8),
                Volume: reader.GetFieldValue<decimal>(9),
                QuoteVolume: reader.GetFieldValue<decimal>(10),
                TradeCount: reader.GetFieldValue<int>(11),
                Exchange: reader.GetString(4)));
        }

        return results;
    }

    public async Task<OrderBookDto?> GetLatestOrderBookAsync(string symbol, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT time, symbol, exchange, update_id, bids, asks
            FROM market.orderbook_snapshots
            WHERE symbol = @symbol
            ORDER BY time DESC
            LIMIT 1
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("symbol", symbol.ToUpperInvariant());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OrderBookDto(
            Symbol: reader.GetString(1),
            UpdateId: reader.GetFieldValue<long>(3),
            Bids: FromJson(reader.GetFieldValue<string>(4)),
            Asks: FromJson(reader.GetFieldValue<string>(5)),
            ExchangeTimestamp: reader.GetFieldValue<DateTimeOffset>(0),
            Exchange: reader.GetString(2));
    }

    public async Task<IndicatorsDto?> GetLatestIndicatorsAsync(
        string symbol, string timeframe, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT symbol, timeframe, indicators, computed_at
            FROM market.indicator_snapshots
            WHERE symbol = @symbol AND timeframe = @timeframe
            ORDER BY time DESC
            LIMIT 1
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("symbol", symbol.ToUpperInvariant());
        command.Parameters.AddWithValue("timeframe", timeframe);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetFieldValue<string>(2), AgentivaJson.Options) ?? [];

        var indicators = raw.ToDictionary(
            kvp => kvp.Key,
            kvp => decimal.Parse(kvp.Value, System.Globalization.CultureInfo.InvariantCulture),
            StringComparer.Ordinal);

        return new IndicatorsDto(
            Symbol: reader.GetString(0),
            Timeframe: reader.GetString(1),
            Indicators: indicators,
            ComputedAt: reader.GetFieldValue<DateTimeOffset>(3));
    }

    private async Task ExecuteAsync(NpgsqlCommand command, string kind, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged and swallowed, not rethrown: a single failed write to a
            // high-frequency append-only table must not tear down the
            // WebSocket receive loop that is still delivering live data for
            // every other symbol and stream.
            logger.LogError(ex, "Failed to persist a {Kind} for {Symbol}.", kind, symbol);
        }
    }

    private static string ToJson(IEnumerable<PriceLevel> levels)
        => System.Text.Json.JsonSerializer.Serialize(
            levels.Select(l => new PriceLevelDto(l.Price.Value, l.Quantity)),
            AgentivaJson.Options);

    private static IReadOnlyList<PriceLevelDto> FromJson(string json)
        => System.Text.Json.JsonSerializer.Deserialize<IReadOnlyList<PriceLevelDto>>(json, AgentivaJson.Options)
           ?? [];
}
