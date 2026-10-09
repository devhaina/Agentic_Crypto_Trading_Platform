using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Execution.Application.Abstractions;
using Agentiva.Execution.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.Execution.Infrastructure.Binance;

/// <summary>
/// Places real orders against the Binance Spot REST API.
/// </summary>
/// <remarks>
/// The only code path in the platform that ever contacts a real exchange with
/// real funds — it is resolved only when the effective trading mode is
/// <see cref="TradingMode.Live"/>, which itself requires the independent
/// <c>Trading:AllowLive</c> flag. See <see cref="IExchangeExecutionResolver"/>.
/// </remarks>
public sealed class BinanceExecutionAdapter(
    HttpClient httpClient,
    IOptions<BinanceOptions> options,
    IClock clock,
    ILogger<BinanceExecutionAdapter> logger)
    : IExchangeExecution
{
    private readonly BinanceOptions _options = options.Value;

    public bool SupportsLiveTrading => true;

    public async Task<Result<ExchangePlacementResult>> PlaceOrderAsync(
        ExchangeOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiKey) || string.IsNullOrEmpty(_options.ApiSecret))
        {
            // Live mode was selected but no credentials are configured. Fail
            // closed rather than attempt an unsigned call that Binance would
            // reject anyway — the distinction matters because the caller
            // treats this as "outcome unknown", and there genuinely is no
            // outcome: nothing was ever sent.
            logger.LogError(
                "Refusing to place order {ClientOrderId}: live trading is enabled but no Binance API "
                + "key/secret is configured.",
                request.ClientOrderId);

            return Result.Failure<ExchangePlacementResult>(Error.Unavailable(
                "execution.binance.no_credentials",
                "Live trading is enabled but no Binance API key/secret is configured."));
        }

        var queryString = BuildSignedQueryString(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v3/order?{queryString}");
        httpRequest.Headers.Add("X-MBX-APIKEY", _options.ApiKey);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            stopwatch.Stop();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = TryParseError(body);

                logger.LogWarning(
                    "Binance rejected order {ClientOrderId} for {Symbol}: {Code} {Message}",
                    request.ClientOrderId,
                    request.Symbol,
                    error.Code,
                    error.Msg);

                return Result.Failure<ExchangePlacementResult>(Error.Validation(
                    "execution.binance.rejected", $"Binance error {error.Code}: {error.Msg}"));
            }

            var parsed = JsonSerializer.Deserialize<BinanceOrderResponse>(body, BinanceJson.Options);

            if (parsed is null)
            {
                return Result.Failure<ExchangePlacementResult>(Error.Unavailable(
                    "execution.binance.empty_response", "Binance returned an empty order response."));
            }

            var (averagePrice, totalFee, feeAsset) = Summarize(parsed);

            return Result.Success(new ExchangePlacementResult(
                ExchangeOrderId: parsed.OrderId.ToString(CultureInfo.InvariantCulture),
                Status: parsed.Status,
                CumulativeFilledQuantity: parsed.ExecutedQty,
                AverageFillPrice: averagePrice,
                CumulativeFeePaid: totalFee,
                FeeAsset: feeAsset,
                SubmissionLatencyMs: (int)stopwatch.ElapsedMilliseconds));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout. The order may already be on the book — this is
            // explicitly an indeterminate outcome, never a rejection.
            logger.LogError(
                ex,
                "Binance order placement timed out for {ClientOrderId}. The outcome is indeterminate.",
                request.ClientOrderId);

            return Result.Failure<ExchangePlacementResult>(Error.Unavailable(
                "execution.binance.timeout", "Binance did not respond in time. The order's true outcome is unknown."));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Could not reach Binance for order {ClientOrderId}.", request.ClientOrderId);

            return Result.Failure<ExchangePlacementResult>(Error.Unavailable(
                "execution.binance.unreachable", "Binance is unreachable. The order's true outcome is unknown."));
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Could not parse Binance's response for order {ClientOrderId}.", request.ClientOrderId);

            return Result.Failure<ExchangePlacementResult>(Error.Unavailable(
                "execution.binance.malformed_response",
                "Binance's response could not be parsed. The order's true outcome is unknown."));
        }
    }

    /// <summary>
    /// Assembles the request's query string and appends its HMAC-SHA256
    /// signature, computed over that exact string.
    /// </summary>
    private string BuildSignedQueryString(ExchangeOrderRequest request)
    {
        var timestamp = clock.UtcNow.ToUnixTimeMilliseconds();

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("symbol", request.Symbol),
            new("side", request.Side == OrderSide.Buy ? "BUY" : "SELL"),
            new("type", request.OrderType == OrderType.Limit ? "LIMIT" : "MARKET"),
            new("quantity", request.Quantity.ToString("0.########", CultureInfo.InvariantCulture)),
            new("newClientOrderId", request.ClientOrderId),

            // FULL is the only response type that returns the fills array a
            // market order needs to report its actual average fill price.
            new("newOrderRespType", "FULL"),
            new("recvWindow", _options.RecvWindowMs.ToString(CultureInfo.InvariantCulture)),
            new("timestamp", timestamp.ToString(CultureInfo.InvariantCulture))
        };

        if (request.OrderType == OrderType.Limit)
        {
            parameters.Add(new("price", request.LimitPrice!.Value.ToString("0.########", CultureInfo.InvariantCulture)));
            parameters.Add(new("timeInForce", "GTC"));
        }

        var unsigned = string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        var signature = BinanceRequestSigner.Sign(unsigned, _options.ApiSecret);

        return $"{unsigned}&signature={signature}";
    }

    private static BinanceErrorResponse TryParseError(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<BinanceErrorResponse>(body, BinanceJson.Options)
                   ?? new BinanceErrorResponse { Msg = body };
        }
        catch (JsonException)
        {
            // Not every error body is JSON (a gateway timeout page, for
            // instance). The raw body is still useful for diagnosis.
            return new BinanceErrorResponse { Msg = body };
        }
    }

    /// <summary>Derives the average fill price and total fee from the response's fills.</summary>
    private static (decimal AveragePrice, decimal TotalFee, string FeeAsset) Summarize(BinanceOrderResponse response)
    {
        var averagePrice = response.ExecutedQty > 0m
            ? response.CummulativeQuoteQty / response.ExecutedQty
            : 0m;

        if (response.Fills is not { Count: > 0 } fills)
        {
            // RESULT response type carries no fills array and therefore no
            // fee breakdown — not expected given newOrderRespType=FULL above,
            // but handled rather than assumed away.
            return (averagePrice, 0m, string.Empty);
        }

        // Every fill within one order is charged in the same asset, so the
        // first fill's asset applies to the summed commission.
        return (averagePrice, fills.Sum(f => f.Commission), fills[0].CommissionAsset);
    }
}
