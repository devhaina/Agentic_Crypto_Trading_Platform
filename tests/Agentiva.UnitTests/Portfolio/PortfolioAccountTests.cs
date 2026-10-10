using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Portfolio.Domain.Accounts;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Portfolio;

/// <summary>Tests for <see cref="PortfolioAccount"/>'s cash-balance arithmetic.</summary>
public sealed class PortfolioAccountTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void A_buy_spends_notional_plus_fee()
    {
        var account = PortfolioAccount.Create(TradingAccountId.New(), 10_000m, "USDT", Now);

        account.ApplyFill(OrderSide.Buy, notional: 5_000m, feePaid: 5m, feeAsset: "USDT", Now);

        account.CashBalance.ShouldBe(4_995m);
    }

    [Fact]
    public void A_sell_receives_notional_minus_fee()
    {
        var account = PortfolioAccount.Create(TradingAccountId.New(), 10_000m, "USDT", Now);

        account.ApplyFill(OrderSide.Sell, notional: 5_000m, feePaid: 5m, feeAsset: "USDT", Now);

        account.CashBalance.ShouldBe(14_995m);
    }

    [Fact]
    public void A_fee_in_a_different_asset_than_the_quote_asset_does_not_move_the_balance()
    {
        var account = PortfolioAccount.Create(TradingAccountId.New(), 10_000m, "USDT", Now);

        account.ApplyFill(OrderSide.Buy, notional: 5_000m, feePaid: 1m, feeAsset: "BNB", Now);

        account.CashBalance.ShouldBe(5_000m);
    }

    [Fact]
    public void Applying_a_fill_raises_a_balance_updated_event()
    {
        var account = PortfolioAccount.Create(TradingAccountId.New(), 10_000m, "USDT", Now);

        account.ApplyFill(OrderSide.Buy, notional: 1_000m, feePaid: 0m, feeAsset: "USDT", Now);

        var @event = account.DomainEvents.Single().ShouldBeOfType<BalanceUpdatedDomainEvent>();
        @event.Free.ShouldBe(9_000m);
        @event.Asset.ShouldBe("USDT");
    }
}
