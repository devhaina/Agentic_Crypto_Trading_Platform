using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Application.Configuration;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Execution.Application.Abstractions;
using Agentiva.Execution.Application.Orders;
using Agentiva.Execution.Domain.Orders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Execution;

/// <summary>
/// Tests for <see cref="SubmitOrderCommandHandler"/>'s mode-based routing and
/// outcome mapping — the actual enforcement point of the trading-mode
/// boundary described in <see cref="TradingOptions"/>.
/// </summary>
public sealed class SubmitOrderCommandHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task Backtest_mode_refuses_without_ever_touching_the_order_store()
    {
        var repository = new FakeOrderRepository { ThrowIfAddCalled = true };
        var handler = CreateHandler(repository, TradingMode.Backtest, new FakeExchangeExecution());

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("execution.order.backtest_mode");
    }

    [Fact]
    public async Task Paper_mode_simulates_an_immediate_fill()
    {
        var repository = new FakeOrderRepository();
        var adapter = new FakeExchangeExecution
        {
            PlacementResult = Result.Success(new ExchangePlacementResult(
                ExchangeOrderId: null, Status: "FILLED", CumulativeFilledQuantity: 1m,
                AverageFillPrice: 50_000m, CumulativeFeePaid: 0m, FeeAsset: "", SubmissionLatencyMs: 0))
        };

        var handler = CreateHandler(repository, TradingMode.Paper, adapter);

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(nameof(OrderStatus.Filled));
        repository.Added.ShouldNotBeNull();
        repository.Added!.Status.ShouldBe(OrderStatus.Filled);
    }

    [Fact]
    public async Task Live_mode_resolves_the_adapter_that_supports_live_trading()
    {
        var repository = new FakeOrderRepository();

        var liveAdapter = new FakeExchangeExecution
        {
            SupportsLiveTrading = true,
            PlacementResult = Result.Success(new ExchangePlacementResult(
                ExchangeOrderId: "99", Status: "NEW", CumulativeFilledQuantity: 0m,
                AverageFillPrice: 0m, CumulativeFeePaid: 0m, FeeAsset: "", SubmissionLatencyMs: 12))
        };

        var simulatedAdapter = new FakeExchangeExecution { ThrowIfCalled = true };

        var resolver = new FakeExchangeExecutionResolver(simulatedAdapter, liveAdapter);

        var handler = new SubmitOrderCommandHandler(
            repository,
            resolver,
            new FakeUnitOfWork(),
            Options.Create(new TradingOptions { Mode = TradingMode.Live, AllowLive = true }),
            new FakeClock(),
            NullLogger<SubmitOrderCommandHandler>.Instance);

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(nameof(OrderStatus.Submitted));
        result.Value.ExchangeOrderId.ShouldBe("99");
    }

    [Fact]
    public async Task An_unavailable_exchange_marks_the_order_indeterminate_rather_than_rejected()
    {
        var repository = new FakeOrderRepository();
        var adapter = new FakeExchangeExecution
        {
            PlacementResult = Result.Failure<ExchangePlacementResult>(
                Error.Unavailable("execution.binance.timeout", "Timed out."))
        };

        var handler = CreateHandler(repository, TradingMode.Paper, adapter);

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(nameof(OrderStatus.Unknown));
    }

    [Fact]
    public async Task An_outright_exchange_rejection_marks_the_order_rejected()
    {
        var repository = new FakeOrderRepository();
        var adapter = new FakeExchangeExecution
        {
            PlacementResult = Result.Failure<ExchangePlacementResult>(
                Error.Validation("execution.binance.rejected", "Insufficient balance."))
        };

        var handler = CreateHandler(repository, TradingMode.Paper, adapter);

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(nameof(OrderStatus.Rejected));
        result.Value.RejectionCode.ShouldBe("execution.binance.rejected");
    }

    private static SubmitOrderCommand Command()
        => new(
            IdempotencyKey: "key-1",
            TradingIntentId: Guid.NewGuid(),
            RiskCheckId: Guid.NewGuid(),
            TradingAccountId: Guid.NewGuid(),
            Symbol: "BTCUSDT",
            Side: "BUY",
            OrderType: "MARKET",
            Quantity: 1m,
            LimitPrice: null,
            ReferencePrice: 50_000m);

    private static SubmitOrderCommandHandler CreateHandler(
        FakeOrderRepository repository, TradingMode mode, FakeExchangeExecution adapter)
        => new(
            repository,
            new FakeExchangeExecutionResolver(adapter, adapter),
            new FakeUnitOfWork(),
            Options.Create(new TradingOptions { Mode = mode }),
            new FakeClock(),
            NullLogger<SubmitOrderCommandHandler>.Instance);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public Order? Added { get; private set; }

        public bool ThrowIfAddCalled { get; set; }

        public void Add(Order order)
        {
            if (ThrowIfAddCalled)
            {
                throw new InvalidOperationException("The order store must not be touched in Backtest mode.");
            }

            Added = order;
        }

        public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Order?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> HasOpenOrderAsync(Symbol symbol, OrderSide side, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Order>> ListRecentAsync(int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeExchangeExecution : IExchangeExecution
    {
        public bool SupportsLiveTrading { get; set; }

        public bool ThrowIfCalled { get; set; }

        public Result<ExchangePlacementResult> PlacementResult { get; set; } = Result.Failure<ExchangePlacementResult>(
            Error.Unavailable("test.not_configured", "The fake adapter has no configured result."));

        public Task<Result<ExchangePlacementResult>> PlaceOrderAsync(
            ExchangeOrderRequest request, CancellationToken cancellationToken)
        {
            if (ThrowIfCalled)
            {
                throw new InvalidOperationException("This adapter must not be used for the resolved trading mode.");
            }

            return Task.FromResult(PlacementResult);
        }
    }

    private sealed class FakeExchangeExecutionResolver(IExchangeExecution simulated, IExchangeExecution live)
        : IExchangeExecutionResolver
    {
        public IExchangeExecution Resolve(TradingMode mode) => mode == TradingMode.Live ? live : simulated;
    }
}
