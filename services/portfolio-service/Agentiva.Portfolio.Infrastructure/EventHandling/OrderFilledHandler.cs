using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.Messaging.Abstractions;
using Agentiva.Contracts.Events;
using Agentiva.Contracts.Events.Orders;
using Agentiva.Portfolio.Application.Abstractions;
using Agentiva.Portfolio.Application.Configuration;
using Agentiva.Portfolio.Domain.Accounts;
using Agentiva.Portfolio.Domain.Positions;
using Agentiva.Portfolio.Domain.Trades;
using Agentiva.Portfolio.Infrastructure.Persistence;
using Agentiva.Portfolio.Infrastructure.TimeSeries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.Portfolio.Infrastructure.EventHandling;

/// <summary>
/// On every complete fill: apply it to the account's cash and to the
/// symbol's position, record a closed trade if the fill closed one, and
/// record one point on the equity curve.
/// </summary>
/// <remarks>
/// Idempotency is handled upstream, not here — the same discipline as
/// <c>MarketCandleCreatedHandler</c>: <c>RabbitMqConsumerService</c> checks
/// this handler's registered name against the inbox before ever calling
/// <see cref="HandleAsync"/>, so a redelivered fill never reaches this method
/// twice.
/// </remarks>
public sealed class OrderFilledHandler(
    IPositionRepository positions,
    IPortfolioAccountRepository accounts,
    ITradeRepository trades,
    IMarkPriceProvider markPrices,
    PortfolioSnapshotWriter snapshotWriter,
    PortfolioDbContext context,
    IOptions<PortfolioOptions> options,
    IClock clock,
    ILogger<OrderFilledHandler> logger)
    : IntegrationEventHandlerBase<OrderFilled>
{
    private readonly PortfolioOptions _options = options.Value;

    public override string EventType => EventTypes.Orders.Filled;

    public override async Task HandleAsync(
        OrderFilled integrationEvent, IntegrationEventContext eventContext, CancellationToken cancellationToken)
    {
        var accountId = TradingAccountId.From(integrationEvent.TradingAccountId);
        var symbol = Symbol.Create(integrationEvent.Symbol);

        var side = string.Equals(integrationEvent.Side, "BUY", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Buy
            : OrderSide.Sell;

        var account = await accounts.GetAsync(accountId, cancellationToken);

        if (account is null)
        {
            account = PortfolioAccount.Create(accountId, _options.StartingCashBalance, _options.QuoteAsset, clock.UtcNow);
            accounts.Add(account);
        }

        var position = await positions.GetAsync(accountId, symbol, cancellationToken);

        if (position is null)
        {
            position = Position.Create(accountId, symbol, _options.QuoteAsset, clock.UtcNow);
            positions.Add(position);
        }

        position.ApplyFill(
            side,
            Quantity.Create(integrationEvent.FilledQuantity),
            Price.Create(integrationEvent.AverageFillPrice),
            integrationEvent.FeePaid,
            integrationEvent.FeeAsset,
            _options.QuoteAsset,
            integrationEvent.FilledAt,
            clock.UtcNow);

        account.ApplyFill(side, integrationEvent.FilledNotional, integrationEvent.FeePaid, integrationEvent.FeeAsset, clock.UtcNow);

        var completedTrade = position.DomainEvents.OfType<TradeCompletedDomainEvent>().SingleOrDefault();

        if (completedTrade is not null)
        {
            trades.Add(Trade.FromCompletedRoundTrip(
                TradeId.From(completedTrade.TradeId),
                accountId,
                symbol,
                Enum.Parse<OrderSide>(completedTrade.Side, ignoreCase: true),
                Price.Create(completedTrade.EntryPrice),
                Price.Create(completedTrade.ExitPrice),
                Quantity.Create(completedTrade.Quantity),
                completedTrade.RealizedPnl,
                completedTrade.TotalFees,
                completedTrade.OpenedAt,
                completedTrade.ClosedAt,
                completedTrade.QuoteAsset));

            logger.LogInformation(
                "Trade {TradeId} closed for account {TradingAccountId} on {Symbol}: realised P&L {RealizedPnl} {QuoteAsset}.",
                completedTrade.TradeId, accountId, symbol, completedTrade.RealizedPnl, completedTrade.QuoteAsset);
        }

        // One SaveChanges for the fill's full effect: the position, the
        // account, and any trade it closed, all together or none of them.
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Applied fill for order {OrderId} to account {TradingAccountId}: {Symbol} {Side} {Quantity} at {Price}.",
            integrationEvent.OrderId, accountId, symbol, side, integrationEvent.FilledQuantity, integrationEvent.AverageFillPrice);

        await WriteEquityCurvePointAsync(accountId, cancellationToken);
    }

    /// <summary>
    /// Marks every open position at a live price and records one row on the
    /// equity curve. Best-effort: see <c>PortfolioSnapshotWriter</c>.
    /// </summary>
    private async Task WriteEquityCurvePointAsync(TradingAccountId accountId, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(accountId, cancellationToken);

        if (account is null)
        {
            return;
        }

        var open = await positions.ListOpenAsync(accountId, cancellationToken);

        var totalExposure = 0m;
        var totalUnrealizedPnl = 0m;

        foreach (var position in open)
        {
            var markPrice = await markPrices.GetMarkPriceAsync(position.Symbol.Value, cancellationToken)
                             ?? position.AverageEntryPrice?.Value ?? 0m;

            var entryPrice = position.AverageEntryPrice?.Value ?? 0m;
            totalExposure += position.Quantity.Value * markPrice;

            totalUnrealizedPnl += position.Direction switch
            {
                PositionDirection.Long => (markPrice - entryPrice) * position.Quantity.Value,
                PositionDirection.Short => (entryPrice - markPrice) * position.Quantity.Value,
                _ => 0m
            };
        }

        // Lifetime, account-wide — not just today's and not just currently
        // open positions, which is why this is a separate sum rather than
        // position.RealizedPnl accumulated in the loop above: a fully closed
        // position drops out of ListOpenAsync but its realised P&L must not.
        var totalRealizedPnl = await positions.SumLifetimeRealizedPnlAsync(accountId, cancellationToken);

        await snapshotWriter.WriteAsync(
            accountId.Value,
            totalValue: account.CashBalance + totalExposure,
            availableBalance: account.CashBalance,
            totalExposure: totalExposure,
            realizedPnl: totalRealizedPnl,
            unrealizedPnl: totalUnrealizedPnl,
            openPositionCount: open.Count,
            quoteAsset: account.QuoteAsset,
            now: clock.UtcNow,
            cancellationToken: cancellationToken);
    }
}
