using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.Contracts.Events.Market;
using Agentiva.MarketData.Application.Contracts;
using Agentiva.MarketData.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.MarketData.Infrastructure.Ingestion;

/// <summary>
/// Periodically checks every configured symbol's data age and publishes
/// <see cref="MarketDataStale"/> on the transition into staleness.
/// </summary>
/// <remarks>
/// Published once per transition, not once per check interval: without the
/// "already reported" guard, a symbol stuck stale for an hour at a five-second
/// check interval would publish seven hundred near-identical events. The event
/// is evidence that something changed, not a heartbeat.
/// </remarks>
public sealed class StalenessMonitorWorker(
    IServiceScopeFactory scopeFactory,
    MarketFeedState feedState,
    IOptions<MarketDataOptions> marketDataOptions,
    IOptions<BinanceOptions> binanceOptions,
    IClock clock,
    ILogger<StalenessMonitorWorker> logger)
    : BackgroundService
{
    private readonly HashSet<string> _reportedStale = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(marketDataOptions.Value.StalenessCheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The outer loop must never die: a transient publish failure
                // on one check must not stop every later check from running.
                logger.LogError(ex, "Staleness check failed; retrying after the usual interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var threshold = TimeSpan.FromSeconds(marketDataOptions.Value.StalenessThresholdSeconds);
        var exchange = binanceOptions.Value.ExchangeName;

        foreach (var status in feedState.GetStatus())
        {
            if (status.IsStale)
            {
                if (_reportedStale.Add(status.Symbol))
                {
                    await PublishStaleAsync(status, threshold, exchange, cancellationToken);
                }
            }
            else
            {
                _reportedStale.Remove(status.Symbol);
            }
        }
    }

    private async Task PublishStaleAsync(
        SymbolFeedStatusDto status,
        TimeSpan threshold,
        string exchange,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Market data for {Symbol} is stale by {StaleForSeconds:0.0}s (threshold {ThresholdSeconds}s).",
            status.Symbol,
            status.StaleForSeconds,
            threshold.TotalSeconds);

        await using var scope = scopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        var lastDataAt = status.LastMessageAt ?? clock.UtcNow;

        var @event = new MarketDataStale
        {
            CorrelationId = Guid.NewGuid().ToString(),
            Symbol = status.Symbol,
            LastDataAt = lastDataAt,
            StaleFor = TimeSpan.FromSeconds(status.StaleForSeconds),
            Threshold = threshold,
            Exchange = exchange
        };

        await publisher.PublishAsync(@event, cancellationToken);
    }
}
