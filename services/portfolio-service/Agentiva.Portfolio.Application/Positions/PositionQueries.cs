using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Portfolio.Application.Abstractions;
using Agentiva.Portfolio.Domain.Positions;

namespace Agentiva.Portfolio.Application.Positions;

/// <summary>Lists every open position for a trading account.</summary>
/// <param name="TradingAccountId">The account to list positions for.</param>
public sealed record GetPositionsQuery(Guid TradingAccountId) : IQuery<Result<IReadOnlyList<PositionResponse>>>;

/// <summary>One open position, marked against a live price.</summary>
/// <param name="PositionId">Position identity.</param>
/// <param name="Symbol">Trading pair.</param>
/// <param name="Direction"><c>LONG</c>, <c>SHORT</c> or <c>FLAT</c>.</param>
/// <param name="Quantity">Current open size.</param>
/// <param name="AverageEntryPrice">Weighted average entry price across every contributing fill.</param>
/// <param name="MarkPrice">Live price this position is currently marked against.</param>
/// <param name="RealizedPnl">Lifetime realised P&amp;L for this symbol, net of quote-asset fees.</param>
/// <param name="UnrealizedPnl">Unrealised P&amp;L at <paramref name="MarkPrice"/>.</param>
/// <param name="QuoteAsset">Asset P&amp;L is denominated in.</param>
/// <param name="OpenedAt">When the current round trip opened.</param>
/// <param name="UpdatedAt">When this position last changed.</param>
public sealed record PositionResponse(
    Guid PositionId,
    string Symbol,
    string Direction,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal MarkPrice,
    decimal RealizedPnl,
    decimal UnrealizedPnl,
    string QuoteAsset,
    DateTimeOffset? OpenedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Handles <see cref="GetPositionsQuery"/>.</summary>
public sealed class GetPositionsQueryHandler(IPositionRepository positions, IMarkPriceProvider markPrices)
    : IRequestHandler<GetPositionsQuery, Result<IReadOnlyList<PositionResponse>>>
{
    public async Task<Result<IReadOnlyList<PositionResponse>>> HandleAsync(
        GetPositionsQuery request, CancellationToken cancellationToken)
    {
        var accountId = TradingAccountId.From(request.TradingAccountId);
        var open = await positions.ListOpenAsync(accountId, cancellationToken);

        var responses = new List<PositionResponse>(open.Count);

        foreach (var position in open)
        {
            var markPrice = await markPrices.GetMarkPriceAsync(position.Symbol.Value, cancellationToken)
                             ?? position.AverageEntryPrice?.Value ?? 0m;

            var entryPrice = position.AverageEntryPrice?.Value ?? 0m;

            var unrealizedPnl = position.Direction switch
            {
                PositionDirection.Long => (markPrice - entryPrice) * position.Quantity.Value,
                PositionDirection.Short => (entryPrice - markPrice) * position.Quantity.Value,
                _ => 0m
            };

            responses.Add(new PositionResponse(
                position.Id.Value,
                position.Symbol.Value,
                position.Direction.ToString().ToUpperInvariant(),
                position.Quantity.Value,
                entryPrice,
                markPrice,
                position.RealizedPnl,
                unrealizedPnl,
                position.QuoteAsset,
                position.OpenedAt,
                position.UpdatedAt));
        }

        return Result.Success<IReadOnlyList<PositionResponse>>(responses);
    }
}
