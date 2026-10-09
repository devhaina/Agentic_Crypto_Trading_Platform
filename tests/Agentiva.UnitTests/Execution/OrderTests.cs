using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Execution.Domain.Orders;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Execution;

/// <summary>Tests for the <see cref="Order"/> aggregate's state machine.</summary>
public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Create_raises_a_created_event_and_starts_in_Created()
    {
        var order = NewOrder();

        order.Status.ShouldBe(OrderStatus.Created);
        order.DomainEvents.ShouldHaveSingleItem();
        order.DomainEvents.Single().ShouldBeOfType<OrderCreatedDomainEvent>();
    }

    [Fact]
    public void Create_rejects_a_zero_quantity()
    {
        Should.Throw<DomainException>(() => Order.Create(
            TradingIntentId.New(), RiskCheckId.New(), TradingAccountId.New(), Symbol.Create("BTCUSDT"),
            OrderSide.Buy, OrderType.Market, Quantity.Zero, null, "AGENTIVA-BTCUSDT-20260101-00000001",
            TradingMode.Paper, Now));
    }

    [Fact]
    public void Create_rejects_a_limit_order_without_a_limit_price()
    {
        Should.Throw<DomainException>(() => Order.Create(
            TradingIntentId.New(), RiskCheckId.New(), TradingAccountId.New(), Symbol.Create("BTCUSDT"),
            OrderSide.Buy, OrderType.Limit, Quantity.Create(1m), null, "AGENTIVA-BTCUSDT-20260101-00000001",
            TradingMode.Paper, Now));
    }

    [Fact]
    public void Submit_moves_to_Submitted_and_raises_the_event()
    {
        var order = NewOrder();
        order.DrainDomainEvents();

        order.Submit("12345", submissionLatencyMs: 42, Now);

        order.Status.ShouldBe(OrderStatus.Submitted);
        order.ExchangeOrderId.ShouldBe("12345");
        order.DomainEvents.Single().ShouldBeOfType<OrderSubmittedDomainEvent>();
    }

    [Fact]
    public void Submit_twice_throws()
    {
        var order = NewOrder();
        order.Submit(null, 0, Now);

        Should.Throw<DomainException>(() => order.Submit(null, 0, Now));
    }

    [Fact]
    public void RecordFill_before_Submit_throws()
    {
        var order = NewOrder();

        Should.Throw<DomainException>(() => order.RecordFill(
            Quantity.Create(1m), Price.Create(100m), 0m, "USDT", Now, Now));
    }

    [Fact]
    public void RecordFill_after_Submit_moves_to_Filled_with_the_correct_notional()
    {
        var order = NewOrder(quantity: 2m);
        order.Submit(null, 0, Now);
        order.DrainDomainEvents();

        order.RecordFill(Quantity.Create(2m), Price.Create(50_000m), feePaid: 4m, feeAsset: "USDT", filledAt: Now, now: Now);

        order.Status.ShouldBe(OrderStatus.Filled);
        order.FilledQuantity.Value.ShouldBe(2m);
        order.AverageFillPrice!.Value.Value.ShouldBe(50_000m);

        var @event = order.DomainEvents.Single().ShouldBeOfType<OrderFilledDomainEvent>();
        @event.FilledNotional.ShouldBe(100_000m);
        @event.FeePaid.ShouldBe(4m);
    }

    [Fact]
    public void RecordFill_rejects_a_fill_larger_than_the_order_quantity()
    {
        var order = NewOrder(quantity: 1m);
        order.Submit(null, 0, Now);

        Should.Throw<DomainException>(() => order.RecordFill(
            Quantity.Create(2m), Price.Create(100m), 0m, "USDT", Now, Now));
    }

    [Fact]
    public void RecordPartialFill_computes_the_remaining_quantity()
    {
        var order = NewOrder(quantity: 10m);
        order.Submit(null, 0, Now);
        order.DrainDomainEvents();

        order.RecordPartialFill(Quantity.Create(4m), Price.Create(100m), 1m, "USDT", Now);

        order.Status.ShouldBe(OrderStatus.PartiallyFilled);
        order.FilledQuantity.Value.ShouldBe(4m);

        var @event = order.DomainEvents.Single().ShouldBeOfType<OrderPartiallyFilledDomainEvent>();
        @event.RemainingQuantity.ShouldBe(6m);
    }

    [Fact]
    public void Reject_after_Submit_throws()
    {
        var order = NewOrder();
        order.Submit(null, 0, Now);

        Should.Throw<DomainException>(() => order.Reject("-2010", "Insufficient balance.", false, Now));
    }

    [Fact]
    public void Reject_from_Created_moves_to_Rejected_and_raises_the_event()
    {
        var order = NewOrder();
        order.DrainDomainEvents();

        order.Reject("-2010", "Insufficient balance.", isRetryable: false, Now);

        order.Status.ShouldBe(OrderStatus.Rejected);
        order.RejectionCode.ShouldBe("-2010");
        order.DomainEvents.Single().ShouldBeOfType<OrderRejectedDomainEvent>();
    }

    [Fact]
    public void MarkIndeterminate_from_Submitted_moves_to_Unknown()
    {
        var order = NewOrder();
        order.Submit(null, 0, Now);
        order.DrainDomainEvents();

        order.MarkIndeterminate("Timed out.", "PlaceOrder", Now);

        order.Status.ShouldBe(OrderStatus.Unknown);
        order.DomainEvents.Single().ShouldBeOfType<OrderIndeterminateDomainEvent>();
    }

    [Fact]
    public void MarkIndeterminate_after_a_fill_throws()
    {
        var order = NewOrder();
        order.Submit(null, 0, Now);
        order.RecordFill(order.Quantity, Price.Create(100m), 0m, "USDT", Now, Now);

        Should.Throw<DomainException>(() => order.MarkIndeterminate("Timed out.", "PlaceOrder", Now));
    }

    [Fact]
    public void Cancel_records_the_quantity_filled_before_cancellation()
    {
        var order = NewOrder(quantity: 10m);
        order.Submit(null, 0, Now);
        order.RecordPartialFill(Quantity.Create(3m), Price.Create(100m), 0m, "USDT", Now);
        order.DrainDomainEvents();

        order.Cancel("Operator request", "operator-1", Now);

        order.Status.ShouldBe(OrderStatus.Cancelled);
        var @event = order.DomainEvents.Single().ShouldBeOfType<OrderCancelledDomainEvent>();
        @event.FilledQuantityBeforeCancel.ShouldBe(3m);
    }

    [Fact]
    public void Cancel_a_filled_order_throws()
    {
        var order = NewOrder();
        order.Submit(null, 0, Now);
        order.RecordFill(order.Quantity, Price.Create(100m), 0m, "USDT", Now, Now);

        Should.Throw<DomainException>(() => order.Cancel("Too late", "operator-1", Now));
    }

    private static Order NewOrder(decimal quantity = 1m)
        => Order.Create(
            TradingIntentId.New(),
            RiskCheckId.New(),
            TradingAccountId.New(),
            Symbol.Create("BTCUSDT"),
            OrderSide.Buy,
            OrderType.Market,
            Quantity.Create(quantity),
            limitPrice: null,
            clientOrderId: "AGENTIVA-BTCUSDT-20260101-00000001",
            tradingMode: TradingMode.Paper,
            now: Now);
}
