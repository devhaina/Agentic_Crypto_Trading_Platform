using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.Execution.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agentiva.Execution.Infrastructure.Providers;

/// <summary>
/// Simulates an immediate fill, for <see cref="Agentiva.BuildingBlocks.Domain.Primitives.TradingMode.Paper"/>.
/// </summary>
/// <remarks>
/// <para>
/// Fills every order completely and instantly at the caller's own reference
/// price, with zero fee. This is deliberately not a backtesting-grade fill
/// model — no slippage, no partial fills, no fee assumption — because that
/// model belongs to the Backtesting Service (Phase 8), which can model it
/// against historical depth rather than guessing. Pretending otherwise here
/// would be a fabricated level of realism; see
/// docs/architecture/known-limitations.md.
/// </para>
/// <para>
/// Never reached in <see cref="Agentiva.BuildingBlocks.Domain.Primitives.TradingMode.Backtest"/>:
/// that mode makes no exchange contact of any kind, simulated or otherwise,
/// and is refused earlier in the workflow — see
/// <c>SubmitOrderCommandHandler</c>.
/// </para>
/// </remarks>
public sealed class SimulatedExchangeExecution(ILogger<SimulatedExchangeExecution> logger) : IExchangeExecution
{
    public bool SupportsLiveTrading => false;

    public Task<Result<ExchangePlacementResult>> PlaceOrderAsync(
        ExchangeOrderRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Simulating an immediate fill for {ClientOrderId} on {Symbol}: {Quantity} at {ReferencePrice}.",
            request.ClientOrderId,
            request.Symbol,
            request.Quantity,
            request.ReferencePrice);

        var result = new ExchangePlacementResult(
            ExchangeOrderId: null,
            Status: "FILLED",
            CumulativeFilledQuantity: request.Quantity,
            AverageFillPrice: request.ReferencePrice,
            CumulativeFeePaid: 0m,
            FeeAsset: string.Empty,
            SubmissionLatencyMs: 0);

        return Task.FromResult(Result.Success(result));
    }
}
