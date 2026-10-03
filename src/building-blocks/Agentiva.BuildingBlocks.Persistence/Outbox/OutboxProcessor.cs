using System.ComponentModel.DataAnnotations;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.BuildingBlocks.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.BuildingBlocks.Persistence.Outbox;

/// <summary>Tuning for the outbox processor.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Whether this service runs an outbox processor.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between polls when the last poll found nothing.</summary>
    [Range(1, 300)]
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>Rows claimed per batch.</summary>
    [Range(1, 1000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Publish attempts before a row is parked as failed.</summary>
    [Range(1, 50)]
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Base seconds for the exponential backoff between attempts.</summary>
    [Range(1, 600)]
    public int BackoffBaseSeconds { get; set; } = 5;
}

/// <summary>
/// Moves pending outbox rows onto the event bus.
/// </summary>
/// <typeparam name="TContext">The service's database context.</typeparam>
/// <remarks>
/// <para>
/// Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>. That matters as soon as
/// a service runs more than one replica: without it, every replica reads the
/// same pending rows and publishes each event several times. <c>SKIP LOCKED</c>
/// lets each replica take a disjoint batch and make progress concurrently,
/// rather than serialising behind a lock.
/// </para>
/// <para>
/// Delivery is at-least-once by construction. A row can be published and then
/// fail to be marked published — a crash in the window between the broker's
/// confirmation and the status update — in which case it is published again.
/// That is why every consumer de-duplicates on event id. The alternative,
/// marking the row first, would be at-most-once and could silently drop a fill.
/// </para>
/// </remarks>
public sealed class OutboxProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    IClock clock,
    ILogger<OutboxProcessor<TContext>> logger)
    : BackgroundService
    where TContext : AgentivaDbContext
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Outbox processor is disabled for {Context}.", typeof(TContext).Name);
            return;
        }

        logger.LogInformation(
            "Outbox processor started for {Context}: batch size {BatchSize}, poll interval {PollInterval}s",
            typeof(TContext).Name,
            _options.BatchSize,
            _options.PollIntervalSeconds);

        var idleDelay = TimeSpan.FromSeconds(_options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await ProcessBatchAsync(stoppingToken);

                // Only idle when the backlog is drained. A full batch means more
                // work is waiting, so poll again immediately instead of
                // sleeping through a burst.
                if (published < _options.BatchSize)
                {
                    await Task.Delay(idleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let the loop die. An unavailable database or broker is a
                // transient condition; a dead processor silently stops every
                // event in the service from being delivered.
                logger.LogError(ex, "Outbox batch failed for {Context}; retrying after the poll interval.",
                    typeof(TContext).Name);

                await Task.Delay(idleDelay, stoppingToken);
            }
        }

        logger.LogInformation("Outbox processor for {Context} stopped.", typeof(TContext).Name);
    }

    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        // The context is configured with EnableRetryOnFailure, and a retrying
        // execution strategy refuses a user-initiated transaction unless the
        // whole unit is wrapped in the strategy — otherwise a mid-transaction
        // retry would replay only part of the work.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            () => ProcessBatchCoreAsync(context, publisher, cancellationToken));
    }

    private async Task<int> ProcessBatchCoreAsync(
        TContext context,
        IEventPublisher publisher,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // SKIP LOCKED is the whole point of the raw SQL: EF cannot express it,
        // and without it multiple replicas duplicate every event.
        var schema = context.Model.GetDefaultSchema() ?? "public";

        // Plain concatenation for the schema (it comes from the EF model, never
        // from user input) and {0}/{1} placeholders for the real parameters, so
        // the timestamp and limit are bound rather than formatted into the SQL.
        var sql =
            "SELECT * FROM \"" + schema + "\".\"outbox_messages\" "
            + "WHERE status = 'Pending' AND (next_attempt_at IS NULL OR next_attempt_at <= {0}) "
            + "ORDER BY created_at "
            + "LIMIT {1} "
            + "FOR UPDATE SKIP LOCKED";

        var batch = await context.OutboxMessages
            .FromSqlRaw(sql, now, _options.BatchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        var publishedCount = 0;

        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishRawAsync(
                    message.EventType,
                    message.Payload,
                    message.EventId,
                    message.CorrelationId,
                    message.EventVersion,
                    message.AgentRunId,
                    cancellationToken);

                message.Status = OutboxStatus.Published;
                message.PublishedAt = clock.UtcNow;
                message.AttemptCount++;
                message.LastError = null;
                message.NextAttemptAt = null;
                publishedCount++;
            }
            catch (Exception ex)
            {
                message.AttemptCount++;
                message.LastError = Truncate($"{ex.GetType().Name}: {ex.Message}", 2000);

                if (message.AttemptCount >= _options.MaxAttempts)
                {
                    message.Status = OutboxStatus.Failed;
                    message.NextAttemptAt = null;

                    logger.LogError(
                        ex,
                        "Outbox row {OutboxId} for {EventType} exhausted {MaxAttempts} attempts and is parked "
                        + "as failed. Correlation id {CorrelationId}.",
                        message.Id,
                        message.EventType,
                        _options.MaxAttempts,
                        message.CorrelationId);
                }
                else
                {
                    // Exponential backoff, capped so a long outage does not push
                    // the next attempt days into the future.
                    var backoffSeconds = Math.Min(
                        _options.BackoffBaseSeconds * Math.Pow(2, message.AttemptCount - 1),
                        TimeSpan.FromMinutes(15).TotalSeconds);

                    message.NextAttemptAt = clock.UtcNow.AddSeconds(backoffSeconds);

                    logger.LogWarning(
                        ex,
                        "Publishing outbox row {OutboxId} for {EventType} failed on attempt {Attempt}; "
                        + "retrying in {BackoffSeconds}s.",
                        message.Id,
                        message.EventType,
                        message.AttemptCount,
                        backoffSeconds);
                }
            }
        }

        // Bypass the override: these are infrastructure rows and raise no
        // domain events of their own.
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return publishedCount;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
