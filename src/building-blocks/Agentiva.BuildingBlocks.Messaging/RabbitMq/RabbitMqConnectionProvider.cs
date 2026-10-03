using Agentiva.BuildingBlocks.Messaging.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Agentiva.BuildingBlocks.Messaging.RabbitMq;

/// <summary>Owns the single long-lived AMQP connection for this service.</summary>
public interface IRabbitMqConnectionProvider : IAsyncDisposable
{
    /// <summary>
    /// Returns the open connection, establishing it on first use and retrying
    /// with a bounded backoff while the broker is still starting.
    /// </summary>
    Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken);

    /// <summary>Whether a usable connection is currently established.</summary>
    bool IsConnected { get; }
}

/// <summary>
/// Default <see cref="IRabbitMqConnectionProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// One connection per service process, many channels on it: that is the
/// topology RabbitMQ is designed for. A connection per operation exhausts
/// broker file descriptors under load.
/// </para>
/// <para>
/// Client-side automatic recovery is enabled, so a broker restart or a network
/// blip re-establishes the connection, channels and consumers without the
/// service restarting. The startup retry loop exists separately, for the
/// ordinary case of a service container winning the race against the broker
/// container during <c>docker compose up</c>.
/// </para>
/// </remarks>
public sealed class RabbitMqConnectionProvider : IRabbitMqConnectionProvider
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnectionProvider(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConnected => _connection is { IsOpen: true };

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsConnected)
        {
            return _connection!;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Re-check: another caller may have connected while we waited.
            if (IsConnected)
            {
                return _connection!;
            }

            _connection = await ConnectWithRetryAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IConnection> ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            Uri = _options.BuildUri(),
            ClientProvidedName = $"agentiva-{_options.ServiceName}",

            // Client-side recovery: reconnect and rebuild channels, queues,
            // bindings and consumers after a broker or network interruption.
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),

            // Detect a dead peer rather than waiting on a half-open socket.
            RequestedHeartbeat = TimeSpan.FromSeconds(30)
        };

        var delay = TimeSpan.FromSeconds(_options.ConnectionRetryDelaySeconds);

        for (var attempt = 1; attempt <= _options.ConnectionRetryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var connection = await factory.CreateConnectionAsync(cancellationToken);

                _logger.LogInformation(
                    "Connected to RabbitMQ at {Broker} on attempt {Attempt}",
                    _options.ToSafeString(),
                    attempt);

                return connection;
            }
            catch (Exception ex) when (attempt < _options.ConnectionRetryAttempts)
            {
                // Logs the sanitised connection string only. BuildUri embeds the
                // password and must never reach a log sink.
                _logger.LogWarning(
                    "RabbitMQ at {Broker} unreachable on attempt {Attempt} of {MaxAttempts}: {Reason}. "
                    + "Retrying in {DelaySeconds}s.",
                    _options.ToSafeString(),
                    attempt,
                    _options.ConnectionRetryAttempts,
                    ex.Message,
                    delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Could not connect to RabbitMQ at {_options.ToSafeString()} after "
            + $"{_options.ConnectionRetryAttempts} attempts.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_connection is not null)
        {
            try
            {
                // Close politely so the broker does not log an abrupt
                // disconnect and so in-flight publishes get confirmed.
                await _connection.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing the RabbitMQ connection during shutdown.");
            }

            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}
