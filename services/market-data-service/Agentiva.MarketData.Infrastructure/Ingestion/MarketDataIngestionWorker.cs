using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.Contracts.Events.Market;
using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;
using Agentiva.MarketData.Domain.Entities;
using Agentiva.MarketData.Infrastructure.Binance;
using Agentiva.MarketData.Infrastructure.Configuration;
using Agentiva.MarketData.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.MarketData.Infrastructure.Ingestion;

/// <summary>
/// Owns every Binance WebSocket connection this service runs, and is where a
/// normalised message is persisted, cached and published.
/// </summary>
/// <remarks>
/// One <see cref="BinanceStreamConnection{TPayload}"/> per (symbol, stream
/// kind): ticker, trade, one kline connection per configured timeframe, and
/// one partial-depth connection. See that type's remarks for why a single
/// multiplexed connection was rejected. Every connection runs for the lifetime
/// of this worker and reconnects on its own; this worker's only job is to wire
/// each one's message handler to the persist/cache/publish pipeline and let
/// them all run concurrently.
/// </remarks>
public sealed class MarketDataIngestionWorker(
    IOptions<MarketDataOptions> marketDataOptions,
    IOptions<BinanceOptions> binanceOptions,
    MarketFeedState feedState,
    TimescaleMarketDataRepository repository,
    IMarketDataCache cache,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILoggerFactory loggerFactory,
    ILogger<MarketDataIngestionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var marketData = marketDataOptions.Value;
        var binance = binanceOptions.Value;
        var symbols = marketData.SymbolList;

        if (symbols.Count == 0)
        {
            logger.LogWarning("No symbols are configured; the market data ingestion worker has nothing to do.");
            return;
        }

        // One event publisher for every connection this worker owns.
        // IEventPublisher is registered scoped by the messaging building
        // block, but RabbitMqEventPublisher serialises its own publishes
        // internally (see its channel gate), so sharing one instance across
        // every concurrently-running connection is safe and avoids resolving
        // a fresh scope on every single market data message.
        await using var scope = scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        var connections = new List<Task>();

        foreach (var symbol in symbols)
        {
            var lowerSymbol = symbol.ToLowerInvariant();

            connections.Add(RunStreamAsync(
                $"{lowerSymbol}@ticker", binance,
                (BinanceTickerPayload payload, CancellationToken ct) => HandleTickerAsync(symbol, binance, publisher, payload, ct),
                stoppingToken));

            connections.Add(RunStreamAsync(
                $"{lowerSymbol}@trade", binance,
                (BinanceTradePayload payload, CancellationToken ct) => HandleTradeAsync(symbol, binance, publisher, payload, ct),
                stoppingToken));

            foreach (var timeframe in marketData.TimeframeList)
            {
                connections.Add(RunStreamAsync(
                    $"{lowerSymbol}@kline_{timeframe}", binance,
                    (BinanceKlineEnvelope payload, CancellationToken ct) => HandleKlineAsync(symbol, binance, publisher, payload, ct),
                    stoppingToken));
            }

            connections.Add(RunStreamAsync(
                $"{lowerSymbol}@depth{binance.OrderBookDepth}@{binance.OrderBookUpdateIntervalMs}ms", binance,
                (BinanceDepthPayload payload, CancellationToken ct) => HandleDepthAsync(symbol, binance, publisher, payload, ct),
                stoppingToken));
        }

        await Task.WhenAll(connections);
    }

    private Task RunStreamAsync<TPayload>(
        string streamName,
        BinanceOptions binance,
        Func<TPayload, CancellationToken, Task> handler,
        CancellationToken stoppingToken)
    {
        var connection = new BinanceStreamConnection<TPayload>(
            binance.BuildStreamUri(streamName),
            streamName,
            binance,
            handler,
            loggerFactory.CreateLogger($"Agentiva.MarketData.Binance[{streamName}]"));

        return connection.RunAsync(stoppingToken);
    }

    private async Task HandleTickerAsync(
        string symbol, BinanceOptions binance, IEventPublisher publisher, BinanceTickerPayload payload, CancellationToken ct)
    {
        feedState.Touch(symbol);

        var tick = Tick.Create(
            payload.Symbol,
            payload.BidPrice,
            payload.BidQuantity,
            payload.AskPrice,
            payload.AskQuantity,
            payload.LastPrice,
            DateTimeOffset.FromUnixTimeMilliseconds(payload.EventTimeMs),
            binance.ExchangeName);

        await repository.WriteTickAsync(tick, ct);

        await cache.SetLatestTickerAsync(
            new TickerDto(
                tick.Symbol.Value, tick.BidPrice.Value, tick.BidQuantity, tick.AskPrice.Value, tick.AskQuantity,
                tick.LastPrice.Value, tick.ExchangeTimestamp, tick.Exchange),
            ct);

        await publisher.PublishAsync(
            new MarketTickCreated
            {
                CorrelationId = Guid.NewGuid().ToString(),
                Symbol = tick.Symbol.Value,
                BidPrice = tick.BidPrice.Value,
                BidQuantity = tick.BidQuantity,
                AskPrice = tick.AskPrice.Value,
                AskQuantity = tick.AskQuantity,
                LastPrice = tick.LastPrice.Value,
                ExchangeTimestamp = tick.ExchangeTimestamp,
                Exchange = tick.Exchange
            },
            ct);
    }

    private async Task HandleTradeAsync(
        string symbol, BinanceOptions binance, IEventPublisher publisher, BinanceTradePayload payload, CancellationToken ct)
    {
        feedState.Touch(symbol);

        var trade = Trade.Create(
            payload.Symbol,
            payload.TradeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            payload.Price,
            payload.Quantity,
            payload.IsBuyerMaker,
            DateTimeOffset.FromUnixTimeMilliseconds(payload.TradeTimeMs),
            binance.ExchangeName);

        await repository.WriteTradeAsync(trade, ct);

        await publisher.PublishAsync(
            new MarketTradeCreated
            {
                CorrelationId = Guid.NewGuid().ToString(),
                Symbol = trade.Symbol.Value,
                ExchangeTradeId = trade.ExchangeTradeId,
                Price = trade.Price.Value,
                Quantity = trade.Quantity,
                BuyerIsMaker = trade.BuyerIsMaker,
                ExchangeTimestamp = trade.ExchangeTimestamp,
                Exchange = trade.Exchange
            },
            ct);
    }

    private async Task HandleKlineAsync(
        string symbol, BinanceOptions binance, IEventPublisher publisher, BinanceKlineEnvelope payload, CancellationToken ct)
    {
        // Binance streams every price update to the current bar, final or
        // not. Only the close is normalised and stored — see the remarks on
        // Candle.Create for why an in-progress bar is never persisted.
        if (!payload.Kline.IsClosed)
        {
            return;
        }

        feedState.Touch(symbol);

        var candle = Candle.Create(
            payload.Symbol,
            payload.Kline.Interval,
            DateTimeOffset.FromUnixTimeMilliseconds(payload.Kline.OpenTimeMs),
            DateTimeOffset.FromUnixTimeMilliseconds(payload.Kline.CloseTimeMs),
            payload.Kline.Open,
            payload.Kline.High,
            payload.Kline.Low,
            payload.Kline.Close,
            payload.Kline.Volume,
            payload.Kline.QuoteVolume,
            payload.Kline.TradeCount,
            binance.ExchangeName);

        await repository.WriteCandleAsync(candle, ct);

        var dto = new CandleDto(
            candle.Symbol.Value, candle.Timeframe.Value, candle.OpenTime, candle.CloseTime, candle.Open.Value,
            candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume, candle.QuoteVolume,
            candle.TradeCount, candle.Exchange);

        await cache.SetLatestCandleAsync(dto, ct);

        await publisher.PublishAsync(
            new MarketCandleCreated
            {
                CorrelationId = Guid.NewGuid().ToString(),
                Symbol = candle.Symbol.Value,
                Timeframe = candle.Timeframe.Value,
                OpenTime = candle.OpenTime,
                CloseTime = candle.CloseTime,
                Open = candle.Open.Value,
                High = candle.High.Value,
                Low = candle.Low.Value,
                Close = candle.Close.Value,
                Volume = candle.Volume,
                QuoteVolume = candle.QuoteVolume,
                TradeCount = candle.TradeCount,
                Exchange = candle.Exchange
            },
            ct);
    }

    private async Task HandleDepthAsync(
        string symbol, BinanceOptions binance, IEventPublisher publisher, BinanceDepthPayload payload, CancellationToken ct)
    {
        feedState.Touch(symbol);

        var now = clock.UtcNow;

        var snapshot = OrderBookSnapshot.Create(
            symbol,
            payload.LastUpdateId,
            ParseLevels(payload.Bids),
            ParseLevels(payload.Asks),
            now,
            binance.ExchangeName);

        await repository.WriteOrderBookAsync(snapshot, ct);

        var dto = new OrderBookDto(
            snapshot.Symbol.Value,
            snapshot.UpdateId,
            snapshot.Bids.Select(l => new PriceLevelDto(l.Price.Value, l.Quantity)).ToArray(),
            snapshot.Asks.Select(l => new PriceLevelDto(l.Price.Value, l.Quantity)).ToArray(),
            snapshot.ExchangeTimestamp,
            snapshot.Exchange);

        await cache.SetLatestOrderBookAsync(dto, ct);

        await publisher.PublishAsync(
            new MarketOrderBookUpdated
            {
                CorrelationId = Guid.NewGuid().ToString(),
                Symbol = dto.Symbol,
                UpdateId = dto.UpdateId,
                Bids = dto.Bids.Select(l => new OrderBookLevel(l.Price, l.Quantity)).ToArray(),
                Asks = dto.Asks.Select(l => new OrderBookLevel(l.Price, l.Quantity)).ToArray(),
                ExchangeTimestamp = dto.ExchangeTimestamp,
                Exchange = dto.Exchange
            },
            ct);
    }

    private static IReadOnlyList<(decimal Price, decimal Quantity)> ParseLevels(List<string[]> levels)
        => levels
            .Select(level => (
                decimal.Parse(level[0], System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(level[1], System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
}
