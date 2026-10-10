using System.Net.Http.Json;
using System.Text.Json;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Json;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.Trading.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agentiva.Trading.Infrastructure.Clients;

/// <summary>HTTP client for the Portfolio Service's snapshot endpoint.</summary>
/// <remarks>
/// Mirrors <see cref="RiskServiceClient"/>: every failure maps to a
/// <see cref="Result"/> failure rather than an exception, so the caller is
/// forced to decide what an unreachable portfolio means for the intent
/// rather than silently proceeding on a fabricated snapshot. Registered
/// without the standard resilience handler for the same reason as the risk
/// gate client — a read that is about to gate a financial decision should
/// fail fast and visibly, not retry quietly and blur how stale the
/// underlying state might be.
/// </remarks>
public sealed class PortfolioServiceClient(
    HttpClient httpClient, ICorrelationContext correlation, ILogger<PortfolioServiceClient> logger)
    : IPortfolioSnapshotProvider
{
    public async Task<Result<PortfolioSnapshotDto>> GetAsync(
        Guid tradingAccountId, string symbol, CancellationToken cancellationToken)
    {
        var query = $"/api/v1/portfolio/snapshot?tradingAccountId={tradingAccountId}&symbol={Uri.EscapeDataString(symbol)}";

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
                    "Portfolio Service returned {StatusCode} for account {TradingAccountId}: {Body}",
                    (int)response.StatusCode, tradingAccountId, Truncate(body, 1000));

                return Result.Failure<PortfolioSnapshotDto>(Error.Unavailable(
                    "trading.portfolio.unavailable",
                    $"The Portfolio Service returned HTTP {(int)response.StatusCode}. No snapshot was obtained."));
            }

            var snapshot = await response.Content.ReadFromJsonAsync<PortfolioSnapshotDto>(
                AgentivaJson.Options, cancellationToken);

            return snapshot is null
                ? Result.Failure<PortfolioSnapshotDto>(Error.Unavailable(
                    "trading.portfolio.empty_response", "The Portfolio Service returned an empty body."))
                : Result.Success(snapshot);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Portfolio Service timed out for account {TradingAccountId}.", tradingAccountId);

            return Result.Failure<PortfolioSnapshotDto>(Error.Unavailable(
                "trading.portfolio.timeout", "The Portfolio Service did not respond in time."));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not reach the Portfolio Service for account {TradingAccountId}.", tradingAccountId);

            return Result.Failure<PortfolioSnapshotDto>(Error.Unavailable(
                "trading.portfolio.unreachable", "The Portfolio Service is unreachable."));
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Could not parse the Portfolio Service's response for account {TradingAccountId}.", tradingAccountId);

            return Result.Failure<PortfolioSnapshotDto>(Error.Unavailable(
                "trading.portfolio.malformed_response", "The Portfolio Service's response could not be parsed."));
        }
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
