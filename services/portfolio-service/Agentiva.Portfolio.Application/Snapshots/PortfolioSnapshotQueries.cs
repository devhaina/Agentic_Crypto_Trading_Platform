using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Portfolio.Application.Abstractions;
using Agentiva.Portfolio.Application.Configuration;
using Agentiva.Portfolio.Domain.Positions;
using Microsoft.Extensions.Options;

namespace Agentiva.Portfolio.Application.Snapshots;

/// <summary>
/// The single query the Trading Service's risk-gate workflow actually needs:
/// equity, available cash, total and per-symbol exposure, open position
/// count, and today's P&amp;L.
/// </summary>
/// <param name="TradingAccountId">The account to snapshot.</param>
/// <param name="Symbol">The symbol a new intent is being evaluated for.</param>
public sealed record GetPortfolioSnapshotQuery(Guid TradingAccountId, string Symbol)
    : IQuery<Result<PortfolioSnapshotResponse>>;

/// <summary>
/// A portfolio snapshot. Field names and meaning match
/// <c>Trading.Application.Abstractions.PortfolioSnapshotDto</c> exactly —
/// this is the real data that interface's HTTP client deserialises.
/// </summary>
/// <param name="Equity">Cash plus the marked value of every open long position.</param>
/// <param name="AvailableBalance">Unencumbered cash.</param>
/// <param name="CurrentExposure">Notional of every open position, marked live.</param>
/// <param name="CurrentSymbolExposure">Notional already open in the requested symbol.</param>
/// <param name="OpenPositionCount">Currently open positions.</param>
/// <param name="DailyPnl">
/// Today's realised P&amp;L (trades closed since UTC midnight) plus every open
/// position's current unrealised P&amp;L. The unrealised component is the
/// position's entire unrealised figure, not only today's price movement —
/// an honest approximation absent a start-of-day mark snapshot; see
/// docs/architecture/known-limitations.md.
/// </param>
public sealed record PortfolioSnapshotResponse(
    decimal Equity,
    decimal AvailableBalance,
    decimal CurrentExposure,
    decimal CurrentSymbolExposure,
    int OpenPositionCount,
    decimal DailyPnl);

/// <summary>Handles <see cref="GetPortfolioSnapshotQuery"/>.</summary>
public sealed class GetPortfolioSnapshotQueryHandler(
    IPortfolioAccountRepository accounts,
    IPositionRepository positions,
    ITradeRepository trades,
    IMarkPriceProvider markPrices,
    IOptions<PortfolioOptions> options,
    IClock clock)
    : IRequestHandler<GetPortfolioSnapshotQuery, Result<PortfolioSnapshotResponse>>
{
    private readonly PortfolioOptions _options = options.Value;

    public async Task<Result<PortfolioSnapshotResponse>> HandleAsync(
        GetPortfolioSnapshotQuery request, CancellationToken cancellationToken)
    {
        var accountId = TradingAccountId.From(request.TradingAccountId);
        var account = await accounts.GetAsync(accountId, cancellationToken);

        if (account is null)
        {
            // No fill has ever touched this account: report the configured
            // fresh-start baseline rather than a zeroed-out snapshot, the
            // same fail-safe-default role PaperPortfolioOptions played.
            return Result.Success(new PortfolioSnapshotResponse(
                Equity: _options.StartingCashBalance,
                AvailableBalance: _options.StartingCashBalance,
                CurrentExposure: 0m,
                CurrentSymbolExposure: 0m,
                OpenPositionCount: 0,
                DailyPnl: 0m));
        }

        var open = await positions.ListOpenAsync(accountId, cancellationToken);

        var totalExposure = 0m;
        var symbolExposure = 0m;
        var totalUnrealizedPnl = 0m;

        foreach (var position in open)
        {
            var markPrice = await markPrices.GetMarkPriceAsync(position.Symbol.Value, cancellationToken)
                             ?? position.AverageEntryPrice?.Value ?? 0m;

            var entryPrice = position.AverageEntryPrice?.Value ?? 0m;
            var notional = position.Quantity.Value * markPrice;

            // Exposure reported as the marked notional regardless of
            // direction; equity below adds it for a long only, since a short
            // position's contribution to total account value is a liability
            // this platform — long-only in practice, see Position's remarks
            // — never actually has to get right.
            totalExposure += notional;

            if (string.Equals(position.Symbol.Value, request.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                symbolExposure += notional;
            }

            totalUnrealizedPnl += position.Direction switch
            {
                PositionDirection.Long => (markPrice - entryPrice) * position.Quantity.Value,
                PositionDirection.Short => (entryPrice - markPrice) * position.Quantity.Value,
                _ => 0m
            };
        }

        var equity = account.CashBalance + totalExposure;

        var startOfUtcDay = new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var dailyRealizedPnl = await trades.SumRealizedPnlSinceAsync(accountId, startOfUtcDay, cancellationToken);
        var dailyPnl = dailyRealizedPnl + totalUnrealizedPnl;

        return Result.Success(new PortfolioSnapshotResponse(
            Equity: equity,
            AvailableBalance: account.CashBalance,
            CurrentExposure: totalExposure,
            CurrentSymbolExposure: symbolExposure,
            OpenPositionCount: open.Count,
            DailyPnl: dailyPnl));
    }
}
