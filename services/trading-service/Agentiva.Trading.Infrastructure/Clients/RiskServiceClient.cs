using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.Trading.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agentiva.Trading.Infrastructure.Clients;

/// <summary>HTTP client for the Risk Service's evaluation endpoint.</summary>
/// <remarks>
/// <para>
/// Every failure mode maps to a <see cref="Result"/> failure rather than an
/// exception, so the caller is forced to handle "no decision" explicitly. The
/// one outcome this client will never produce is a fabricated approval.
/// </para>
/// <para>
/// Registered <em>without</em> the standard resilience handler. The shared
/// handler retries automatically, which is right for idempotent reads and wrong
/// here: a timed-out evaluation may have completed, so an automatic retry risks
/// a second approval. The idempotency key makes a deliberate retry safe, but the
/// decision belongs to an operator, not to a policy.
/// </para>
/// </remarks>
public sealed class RiskServiceClient(
    HttpClient httpClient,
    ICorrelationContext correlation,
    ILogger<RiskServiceClient> logger)
    : IRiskServiceClient
{
    public async Task<Result<RiskDecisionResult>> EvaluateAsync(
        RiskEvaluationRequestDto request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/risk/evaluations")
        {
            Content = JsonContent.Create(request, options: AgentivaJson.Options)
        };

        message.Headers.Add(CorrelationHeaders.IdempotencyKey, idempotencyKey);
        message.Headers.Add(CorrelationHeaders.CorrelationId, correlation.CorrelationId);

        if (!string.IsNullOrEmpty(correlation.AgentRunId))
        {
            message.Headers.Add(CorrelationHeaders.AgentRunId, correlation.AgentRunId);
        }

        // Delegation, not a separately minted service token: the risk gate
        // sees the same identity the gateway already authenticated. See the
        // remarks on ICorrelationContext.AuthorizationHeader.
        if (!string.IsNullOrEmpty(correlation.AuthorizationHeader))
        {
            message.Headers.TryAddWithoutValidation("Authorization", correlation.AuthorizationHeader);
        }

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                // A replayed or poisoned idempotency key. Surfaced as a conflict
                // rather than swallowed: the caller must not treat it as a fresh
                // rejection or retry it blindly.
                var conflictBody = await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogWarning(
                    "Risk gate reported an idempotency conflict for key {IdempotencyKey}: {Body}",
                    idempotencyKey,
                    Truncate(conflictBody, 500));

                return Result.Failure<RiskDecisionResult>(Error.Conflict(
                    "trading.risk.idempotency_conflict",
                    "The risk gate reported an idempotency conflict for this intent. "
                    + "Resolve it before reissuing."));
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

                logger.LogError(
                    "Risk gate returned {StatusCode} for intent {TradingIntentId}: {Body}",
                    (int)response.StatusCode,
                    request.TradingIntentId,
                    Truncate(errorBody, 1000));

                return Result.Failure<RiskDecisionResult>(Error.Unavailable(
                    "trading.risk.unavailable",
                    $"The risk gate returned HTTP {(int)response.StatusCode}. No decision was made."));
            }

            var decision = await response.Content.ReadFromJsonAsync<RiskEvaluationResponseDto>(
                AgentivaJson.Options, cancellationToken);

            if (decision is null)
            {
                return Result.Failure<RiskDecisionResult>(Error.Unavailable(
                    "trading.risk.empty_response",
                    "The risk gate returned an empty body. No decision was made."));
            }

            return Result.Success(new RiskDecisionResult(
                decision.RiskCheckId,
                decision.Decision,
                decision.ApprovedQuantity,
                decision.ApprovedNotional,
                decision.EffectiveEntryPrice,
                decision.RiskAmount,
                decision.RejectionCodes ?? []));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout. The evaluation may well have completed on the other
            // side, so this is explicitly an indeterminate outcome and never an
            // approval. The caller parks the intent as RiskUnavailable.
            logger.LogError(
                ex,
                "Risk gate timed out for intent {TradingIntentId}. The outcome is indeterminate; "
                + "the intent will not be executed.",
                request.TradingIntentId);

            return Result.Failure<RiskDecisionResult>(Error.Unavailable(
                "trading.risk.timeout",
                "The risk gate did not respond in time. The evaluation outcome is unknown, so the "
                + "intent is not executable."));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(
                ex,
                "Could not reach the risk gate for intent {TradingIntentId}.",
                request.TradingIntentId);

            return Result.Failure<RiskDecisionResult>(Error.Unavailable(
                "trading.risk.unreachable",
                "The risk gate is unreachable. No decision was made and the intent is not executable."));
        }
        catch (JsonException ex)
        {
            logger.LogError(
                ex,
                "Could not parse the risk gate's response for intent {TradingIntentId}.",
                request.TradingIntentId);

            return Result.Failure<RiskDecisionResult>(Error.Unavailable(
                "trading.risk.malformed_response",
                "The risk gate's response could not be parsed. No decision was recorded."));
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>Wire shape of the risk gate's response.</summary>
    private sealed record RiskEvaluationResponseDto(
        Guid RiskCheckId,
        Guid TradingIntentId,
        Guid RiskPolicyId,
        string Decision,
        decimal ApprovedQuantity,
        decimal ApprovedNotional,
        decimal EffectiveEntryPrice,
        decimal RiskAmount,
        decimal RiskBudget,
        string BindingConstraint,
        IReadOnlyList<string>? RejectionCodes,
        IReadOnlyList<RiskCheckItemDto>? Checks);

    private sealed record RiskCheckItemDto(
        string CheckName,
        bool Passed,
        string Code,
        string Detail,
        string? ObservedValue,
        string? LimitValue);
}
