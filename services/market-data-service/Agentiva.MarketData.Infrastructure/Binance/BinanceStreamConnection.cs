using System.Net.WebSockets;
using System.Text.Json;
using Agentiva.MarketData.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace Agentiva.MarketData.Infrastructure.Binance;

/// <summary>
/// Owns one WebSocket connection to one Binance raw stream, reconnecting with
/// exponential backoff for as long as the service runs.
/// </summary>
/// <typeparam name="TPayload">Shape of this stream's message, e.g. <see cref="BinanceTickerPayload"/>.</typeparam>
/// <remarks>
/// <para>
/// One connection per (symbol, stream kind) rather than one multiplexed
/// connection per symbol or per service. Binance's partial depth payload
/// carries neither a symbol nor a stream name, so the only reliable way to
/// know which symbol a depth message belongs to is the connection it arrived
/// on — multiplexing would make that information-less payload ambiguous. The
/// simpler, uniform connection-per-stream shape is kept for every stream kind,
/// trading a modest number of extra open sockets for a receive loop that
/// needs no message-type sniffing anywhere.
/// </para>
/// <para>
/// Modelled after <c>RabbitMqConnectionProvider</c> and <c>OutboxProcessor</c>:
/// a connection fault is logged and retried with backoff, never allowed to
/// terminate the loop. A market data feed that silently stops after one
/// network blip is worse than one that keeps trying loudly.
/// </para>
/// </remarks>
public sealed class BinanceStreamConnection<TPayload>(
    Uri uri,
    string streamName,
    BinanceOptions options,
    Func<TPayload, CancellationToken, Task> onMessage,
    ILogger logger)
{
    private const int ReceiveBufferSize = 32 * 1024;

    /// <summary>Runs the connect/receive/reconnect loop until cancelled. Never throws.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(options.InitialReconnectDelaySeconds);
        var maxDelay = TimeSpan.FromSeconds(options.MaxReconnectDelaySeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();

            try
            {
                await socket.ConnectAsync(uri, stoppingToken);

                logger.LogInformation("Connected to Binance stream {Stream}.", streamName);

                // A connection that stays open resets the backoff: only
                // repeated, immediate failures should wait longer and longer.
                delay = TimeSpan.FromSeconds(options.InitialReconnectDelaySeconds);

                await ReceiveLoopAsync(socket, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Binance stream {Stream} disconnected; reconnecting in {DelaySeconds}s.",
                    streamName,
                    delay.TotalSeconds);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
        }

        logger.LogInformation("Binance stream {Stream} stopped.", streamName);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken stoppingToken)
    {
        var buffer = new byte[ReceiveBufferSize];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", stoppingToken);
                    return;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            message.Position = 0;

            try
            {
                var payload = await JsonSerializer.DeserializeAsync<TPayload>(
                    message, BinanceJson.Options, stoppingToken);

                if (payload is not null)
                {
                    await onMessage(payload, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A malformed message or a failure while handling one
                // (persisting it, caching it, publishing it) must not kill the
                // socket read loop — the next message on the same connection
                // is unrelated and should still be processed.
                logger.LogWarning(ex, "Failed to process a message on Binance stream {Stream}.", streamName);
            }
        }
    }
}
