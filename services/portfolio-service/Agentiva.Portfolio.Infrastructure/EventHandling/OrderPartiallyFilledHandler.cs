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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agentiva.Portfolio.Infrastructure.EventHandling;

/// <summary>
/// On a partial fill: apply it to the account's cash and position exactly
/// like <see cref="OrderFilledHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>OrderPartiallyFilled.CumulativeFilledQuantity</c> is cumulative across
/// the order's whole life, not this delivery's increment — intentionally, so
/// the event itself is idempotent on redelivery (see its remarks in
/// <c>contracts/events/.../Orders/OrderEvents.cs</c>). This handler applies
/// it as if it were the increment, which is only correct because no code
/// path in the platform today raises more than one
/// <c>OrderPartiallyFilled</c> per order — Phase 5's <c>SubmitOrderCommandHandler</c>
/// derives it from a single synchronous placement response, never from a
/// later poll. If a future phase adds one (a limit order resting on the
/// book, filling across several deliveries), this handler needs to track
/// each order's previously-applied cumulative quantity and apply only the
/// difference — it does not yet, because nothing can exercise the gap today.
/// </para>
/// </remarks>
public sealed class OrderPartiallyFilledHandler(
    IPositionRepository positions,
    IPortfolioAccountRepository accounts,
    ITradeRepository trades,
    PortfolioDbContext context,
    IOptions<PortfolioOptions> options,
    IClock clock,
    ILogger<OrderPartiallyFilledHandler> logger)
    : IntegrationEventHandlerBase<OrderPartiallyFilled>
{
    private readonly PortfolioOptions _options = options.Value;

    public override string EventType => EventTypes.Orders.PartiallyFilled;

    public override async Task HandleAsync(
        OrderPartiallyFilled integrationEvent, IntegrationEventContext eventContext, CancellationToken cancellationToken)
    {
        var accountId = TradingAccountId.From(integrationEvent.TradingAccountId);
        var symbol = Symbol.Create(integrationEvent.Symbol);

        // OrderPartiallyFilled does not carry the order's side directly, but
        // a position being extended or reduced can be inferred only with the
        // side — so this event alone is not enough without it. It is instead
        // derived the only way available here: a positive remaining quantity
        // with a position already open on the opposite side implies a
        // reducing fill, otherwise an extending one. This is a narrower
        // inference than OrderFilledHandler needs, because that event does
        // carry Side directly.
        var position = await positions.GetAsync(accountId, symbol, cancellationToken);

        var side = position is { Direction: not PositionDirection.Flat } existing
            ? existing.Direction == PositionDirection.Long ? OrderSide.Sell : OrderSide.Buy
            : OrderSide.Buy;

        var account = await accounts.GetAsync(accountId, cancellationToken);

        if (account is null)
        {
            account = PortfolioAccount.Create(accountId, _options.StartingCashBalance, _options.QuoteAsset, clock.UtcNow);
            accounts.Add(account);
        }

        if (position is null)
        {
            position = Position.Create(accountId, symbol, _options.QuoteAsset, clock.UtcNow);
            positions.Add(position);
        }

        var fillQuantity = Quantity.Create(integrationEvent.CumulativeFilledQuantity);
        var fillPrice = Price.Create(integrationEvent.AverageFillPrice);
        var notional = fillQuantity.Value * fillPrice.Value;

        position.ApplyFill(
            side, fillQuantity, fillPrice, integrationEvent.CumulativeFeePaid, integrationEvent.FeeAsset,
            _options.QuoteAsset, clock.UtcNow, clock.UtcNow);

        account.ApplyFill(side, notional, integrationEvent.CumulativeFeePaid, integrationEvent.FeeAsset, clock.UtcNow);

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
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Applied partial fill for order {OrderId} to account {TradingAccountId}: {Symbol} cumulative {Quantity} at {Price}.",
            integrationEvent.OrderId, accountId, symbol, integrationEvent.CumulativeFilledQuantity, integrationEvent.AverageFillPrice);
    }
}
