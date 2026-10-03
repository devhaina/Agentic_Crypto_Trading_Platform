using System.Text.Json;
using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.Common.Results;
using Microsoft.Extensions.Logging;

namespace Agentiva.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Guarantees that a command implementing <see cref="IFinancialCommand{TResponse}"/>
/// takes effect at most once, however many times it is delivered.
/// </summary>
/// <remarks>
/// <para>
/// A command can arrive more than once for entirely routine reasons: an HTTP
/// client retried after a timeout, RabbitMQ redelivered an unacknowledged
/// message, a pod restarted mid-handler. Without a guard, each delivery places
/// another exchange order.
/// </para>
/// <para>
/// The three outcomes of a replay are deliberately different:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Completed</b> — the stored response is returned verbatim. The caller
///     cannot tell a replay from the original, which is the point.
///   </description></item>
///   <item><description>
///     <b>In flight</b> — rejected with a conflict. The first attempt is still
///     running; running a second concurrently is exactly the duplicate this
///     guard exists to prevent.
///   </description></item>
///   <item><description>
///     <b>Failed</b> — also rejected, and this is the important case. A handler
///     that threw may or may not have reached the exchange first. Automatically
///     retrying would risk a second live order, so the key stays poisoned and
///     the operation is handed to reconciliation. Refusing to guess is the only
///     safe behavior when the true state is unknown.
///   </description></item>
/// </list>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IIdempotencyStore store,
    ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only financial commands are guarded. Queries and non-financial
        // commands pass straight through so the hot read path stays free of a
        // database round trip.
        if (request is not IFinancialCommand<TResponse> financial)
        {
            return await next(cancellationToken);
        }

        var key = financial.IdempotencyKey;
        var requestType = typeof(TRequest).FullName ?? typeof(TRequest).Name;

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"{requestType} is a financial command but supplied no idempotency key.");
        }

        var claimed = await store.TryClaimAsync(key, requestType, cancellationToken);

        if (!claimed)
        {
            var existing = await store.FindAsync(key, cancellationToken)
                           ?? throw new InvalidOperationException(
                               $"Idempotency key '{key}' could not be claimed but no record exists. "
                               + "This indicates the idempotency store is inconsistent.");

            return ReplayOf(existing, key, requestType);
        }

        try
        {
            var response = await next(cancellationToken);

            var payload = JsonSerializer.Serialize(response, AgentivaJson.Options);
            await store.CompleteAsync(key, payload, cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            // Deliberately does not release the key. See the type remarks: the
            // outcome is unknown, and a retry could duplicate a live order.
            await store.MarkFailedAsync(key, $"{ex.GetType().Name}: {ex.Message}", CancellationToken.None);

            logger.LogError(
                ex,
                "Financial command {RequestType} failed with idempotency key {IdempotencyKey} poisoned. "
                + "The key will not be retried automatically and requires reconciliation.",
                requestType,
                key);

            throw;
        }
    }

    private TResponse ReplayOf(IdempotencyRecord existing, string key, string requestType)
    {
        switch (existing.State)
        {
            case IdempotencyState.Completed:
                logger.LogInformation(
                    "Replaying stored response for {RequestType} with idempotency key {IdempotencyKey}; "
                    + "no new side effect was produced.",
                    requestType,
                    key);

                if (existing.ResponsePayload is null)
                {
                    // A void-returning command. Nothing to replay, and nothing to redo.
                    return default!;
                }

                return JsonSerializer.Deserialize<TResponse>(existing.ResponsePayload, AgentivaJson.Options)
                       ?? throw new InvalidOperationException(
                           $"Stored idempotent response for key '{key}' could not be deserialised to "
                           + $"{typeof(TResponse).Name}. The command's response shape has changed "
                           + "incompatibly since the record was written.");

            case IdempotencyState.InFlight:
                return FailureOrThrow(Error.Conflict(
                    "idempotency.in_flight",
                    $"A request with idempotency key '{key}' is still in progress. "
                    + "Retry once it completes rather than issuing a concurrent duplicate."));

            case IdempotencyState.Failed:
                return FailureOrThrow(Error.Conflict(
                    "idempotency.poisoned",
                    $"A previous request with idempotency key '{key}' failed with an indeterminate "
                    + "outcome and will not be retried automatically. Reason: "
                    + $"{existing.FailureReason}. Resolve it through reconciliation, then reissue "
                    + "with a new key."));

            default:
                throw new InvalidOperationException(
                    $"Unhandled idempotency state {existing.State} for key '{key}'.");
        }
    }

    /// <summary>
    /// Returns the error as a failed <c>Result</c> when the command's response is
    /// one, and throws otherwise. Commands returning <c>Result</c> get a clean
    /// 409 through normal error mapping; anything else has no channel for an
    /// expected failure, so an exception is the only correct option.
    /// </summary>
    private static TResponse FailureOrThrow(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        if (typeof(TResponse).IsGenericType
            && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = typeof(TResponse).GetGenericArguments()[0];

            var failureMethod = typeof(Result)
                .GetMethods()
                .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(valueType);

            return (TResponse)failureMethod.Invoke(null, [error])!;
        }

        throw new IdempotencyConflictException(error.Code, error.Message);
    }
}

/// <summary>
/// Raised when a replayed financial command cannot proceed. Mapped to HTTP 409.
/// </summary>
public sealed class IdempotencyConflictException(string code, string message) : Exception(message)
{
    /// <summary>Stable reason code, e.g. <c>idempotency.poisoned</c>.</summary>
    public string Code { get; } = code;
}
