using System.Net.Http.Json;
using System.Text.Json;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.Trading.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agentiva.Trading.Infrastructure.Clients;

/// <summary>HTTP client for the Execution Service.</summary>
/// <remarks>
/// Registered without the standard resilience handler, for the same reason as
/// <see cref="RiskServiceClient"/>: an automatic retry of a timed-out order
/// placement could place a second one. See the type remarks there.
/// </remarks>
public sealed class ExecutionServiceClient(
    HttpClient httpClient,
    ICorrelationContext correlation,
    ILogger<ExecutionServiceClient> logger)
    : IExecutionServiceClient
{
    public async Task<Result<bool>> HasOpenOrderAsync(
        string symbol, string side, CancellationToken cancellationToken)
    {
        var query = $"/api/v1/orders/open?symbol={Uri.EscapeDataString(symbol)}&side={Uri.EscapeDataString(side)}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, query);

            // Delegation, not a separately minted service token — see the
            // remarks on ICorrelationContext.AuthorizationHeader.
            if (!string.IsNullOrEmpty(correlation.AuthorizationHeader))
            {
                request.Headers.TryAddWithoutValidation("Authorization", correlation.AuthorizationHeader);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogError(
                    "Execution Service returned {StatusCode} checking for an open order on {Symbol} {Side}: {Body}",
                    (int)response.StatusCode, symbol, side, Truncate(body, 1000));

                return Result.Failure<bool>(Error.Unavailable(
                    "trading.execution.unavailable",
                    $"The Execution Service returned HTTP {(int)response.StatusCode} checking for an open "
                    + "order."));
            }

            var decoded = await response.Content.ReadFromJsonAsync<HasOpenOrderResponseDto>(
                AgentivaJson.Options, cancellationToken);

            return decoded is null
                ? Result.Failure<bool>(Error.Unavailable(
                    "trading.execution.empty_response", "The Execution Service returned an empty body."))
                : Result.Success(decoded.HasOpenOrder);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Execution Service timed out checking for an open order on {Symbol} {Side}.", symbol, side);

            return Result.Failure<bool>(Error.Unavailable(
                "trading.execution.timeout", "The Execution Service did not respond in time."));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not reach the Execution Service checking for an open order on {Symbol} {Side}.", symbol, side);

            return Result.Failure<bool>(Error.Unavailable(
                "trading.execution.unreachable", "The Execution Service is unreachable."));
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Could not parse the Execution Service's open-order response for {Symbol} {Side}.", symbol, side);

            return Result.Failure<bool>(Error.Unavailable(
                "trading.execution.malformed_response", "The Execution Service's response could not be parsed."));
        }
    }

    public async Task<Result<ExecutionOrderResult>> SubmitOrderAsync(
        ExecutionOrderRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(request, options: AgentivaJson.Options)
        };

        message.Headers.Add(CorrelationHeaders.IdempotencyKey, idempotencyKey);
        message.Headers.Add(CorrelationHeaders.CorrelationId, correlation.CorrelationId);

        if (!string.IsNullOrEmpty(correlation.AuthorizationHeader))
        {
            message.Headers.TryAddWithoutValidation("Authorization", correlation.AuthorizationHeader);
        }

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogError(
                    "Execution Service returned {StatusCode} for intent {TradingIntentId}: {Body}",
                    (int)response.StatusCode, request.TradingIntentId, Truncate(body, 1000));

                return Result.Failure<ExecutionOrderResult>(Error.Unavailable(
                    "trading.execution.unavailable",
                    $"The Execution Service returned HTTP {(int)response.StatusCode}. No order was placed."));
            }

            var order = await response.Content.ReadFromJsonAsync<OrderResponseDto>(
                AgentivaJson.Options, cancellationToken);

            return order is null
                ? Result.Failure<ExecutionOrderResult>(Error.Unavailable(
                    "trading.execution.empty_response", "The Execution Service returned an empty body."))
                : Result.Success(new ExecutionOrderResult(order.OrderId, order.Status, order.FilledQuantity));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout. The order may already have been placed, so this is
            // explicitly an indeterminate outcome and never a success.
            logger.LogError(
                ex,
                "Execution Service timed out for intent {TradingIntentId}. The outcome is indeterminate.",
                request.TradingIntentId);

            return Result.Failure<ExecutionOrderResult>(Error.Unavailable(
                "trading.execution.timeout",
                "The Execution Service did not respond in time. The order's true outcome is unknown."));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not reach the Execution Service for intent {TradingIntentId}.", request.TradingIntentId);

            return Result.Failure<ExecutionOrderResult>(Error.Unavailable(
                "trading.execution.unreachable", "The Execution Service is unreachable."));
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Could not parse the Execution Service's response for intent {TradingIntentId}.", request.TradingIntentId);

            return Result.Failure<ExecutionOrderResult>(Error.Unavailable(
                "trading.execution.malformed_response", "The Execution Service's response could not be parsed."));
        }
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record HasOpenOrderResponseDto(bool HasOpenOrder);

    /// <summary>
    /// Wire shape of the Execution Service's order response. Every field of
    /// <c>Execution.Application.Orders.OrderResponse</c> must appear here:
    /// <see cref="AgentivaJson"/> rejects unmapped members on the way in.
    /// </summary>
    private sealed record OrderResponseDto(
        Guid OrderId,
        Guid TradingIntentId,
        string Symbol,
        string Side,
        string OrderType,
        decimal Quantity,
        decimal? LimitPrice,
        string ClientOrderId,
        string? ExchangeOrderId,
        string Status,
        decimal FilledQuantity,
        decimal? AverageFillPrice,
        decimal FeePaid,
        string? FeeAsset,
        string TradingMode,
        string? RejectionCode,
        string? RejectionDetail,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
