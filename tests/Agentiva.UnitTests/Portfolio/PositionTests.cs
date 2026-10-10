using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Portfolio.Domain.Positions;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Portfolio;

/// <summary>
/// Tests for <see cref="Position"/>'s average-cost accounting: extending,
/// partially closing, fully closing, and flipping through zero.
/// </summary>
public sealed class PositionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Opening_a_position_sets_the_average_entry_price_and_direction()
    {
        var position = NewPosition();

        position.ApplyFill(OrderSide.Buy, Quantity.Create(1m), Price.Create(50_000m), 10m, "USDT", "USDT", Now, Now);

        position.Direction.ShouldBe(PositionDirection.Long);
        position.Quantity.Value.ShouldBe(1m);
        position.AverageEntryPrice!.Value.Value.ShouldBe(50_000m);
        position.RealizedPnl.ShouldBe(0m);
        position.OpenedAt.ShouldBe(Now);
    }

    [Fact]
    public void Adding_to_a_position_reweights_the_average_entry_price()
    {
        var position = NewPosition();
        position.ApplyFill(OrderSide.Buy, Quantity.Create(1m), Price.Create(50_000m), 10m, "USDT", "USDT", Now, Now);

        position.ApplyFill(OrderSide.Buy, Quantity.Create(1m), Price.Create(52_000m), 10m, "USDT", "USDT", Now, Now);

        position.Quantity.Value.ShouldBe(2m);
        position.AverageEntryPrice!.Value.Value.ShouldBe(51_000m);
    }

    [Fact]
    public void A_partial_close_realises_PnL_on_the_closed_slice_only_and_keeps_the_average_entry_price()
    {
        var position = NewPosition();
        position.ApplyFill(OrderSide.Buy, Quantity.Create(2m), Price.Create(50_000m), 0m, "USDT", "USDT", Now, Now);

        position.ApplyFill(OrderSide.Sell, Quantity.Create(1m), Price.Create(53_000m), 5m, "USDT", "USDT", Now, Now);

        position.Direction.ShouldBe(PositionDirection.Long);
        position.Quantity.Value.ShouldBe(1m);
        position.AverageEntryPrice!.Value.Value.ShouldBe(50_000m);
        position.RealizedPnl.ShouldBe(3_000m);
        position.DomainEvents.OfType<TradeCompletedDomainEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void Fully_closing_a_position_raises_TradeCompleted_with_the_round_trips_own_totals_and_resets_to_flat()
    {
        var position = NewPosition();
        position.ApplyFill(OrderSide.Buy, Quantity.Create(2m), Price.Create(50_000m), 20m, "USDT", "USDT", Now, Now);
        position.DrainDomainEvents();

        position.ApplyFill(OrderSide.Sell, Quantity.Create(1m), Price.Create(53_000m), 5m, "USDT", "USDT", Now, Now);
        position.DrainDomainEvents();

        position.ApplyFill(OrderSide.Sell, Quantity.Create(1m), Price.Create(54_000m), 5m, "USDT", "USDT", Now, Now);

        position.Direction.ShouldBe(PositionDirection.Flat);
        position.Quantity.Value.ShouldBe(0m);
        position.AverageEntryPrice.ShouldBeNull();

        // Lifetime: (53000-50000)*1 + (54000-50000)*1 = 3000 + 4000.
        position.RealizedPnl.ShouldBe(7_000m);

        var trade = position.DomainEvents.OfType<TradeCompletedDomainEvent>().Single();
        trade.EntryPrice.ShouldBe(50_000m);
        trade.ExitPrice.ShouldBe(53_500m); // (53000*1 + 54000*1) / 2
        trade.Quantity.ShouldBe(2m);
        trade.RealizedPnl.ShouldBe(7_000m);
        trade.TotalFees.ShouldBe(30m); // 20 (opening) + 5 + 5 (both closing fills)
        trade.Side.ShouldBe("BUY");
    }

    [Fact]
    public void A_fill_that_overshoots_the_open_quantity_closes_the_round_trip_and_flips_direction()
    {
        var position = NewPosition();
        position.ApplyFill(OrderSide.Buy, Quantity.Create(1m), Price.Create(100m), 0m, "USDT", "USDT", Now, Now);
        position.DrainDomainEvents();

        position.ApplyFill(OrderSide.Sell, Quantity.Create(3m), Price.Create(110m), 6m, "USDT", "USDT", Now, Now);

        var trade = position.DomainEvents.OfType<TradeCompletedDomainEvent>().Single();
        trade.Quantity.ShouldBe(1m);
        trade.RealizedPnl.ShouldBe(10m); // (110-100)*1

        position.Direction.ShouldBe(PositionDirection.Short);
        position.Quantity.Value.ShouldBe(2m);
        position.AverageEntryPrice!.Value.Value.ShouldBe(110m);
    }

    [Fact]
    public void A_fee_in_a_different_asset_than_the_quote_asset_does_not_affect_realised_PnL()
    {
        var position = NewPosition();
        position.ApplyFill(OrderSide.Buy, Quantity.Create(1m), Price.Create(100m), 0m, "USDT", "USDT", Now, Now);

        position.ApplyFill(OrderSide.Sell, Quantity.Create(1m), Price.Create(110m), 1m, "BNB", "USDT", Now, Now);

        // Realised P&L is the price difference alone: the BNB fee is not converted.
        position.RealizedPnl.ShouldBe(10m);
    }

    private static Position NewPosition()
        => Position.Create(TradingAccountId.New(), Symbol.Create("BTCUSDT"), "USDT", Now);
}
