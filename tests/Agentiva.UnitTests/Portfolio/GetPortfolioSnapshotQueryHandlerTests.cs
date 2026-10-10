using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Portfolio.Application.Abstractions;
using Agentiva.Portfolio.Application.Configuration;
using Agentiva.Portfolio.Application.Snapshots;
using Agentiva.Portfolio.Domain.Accounts;
using Agentiva.Portfolio.Domain.Positions;
using Agentiva.Portfolio.Domain.Trades;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.Portfolio;

/// <summary>
/// Tests for <see cref="GetPortfolioSnapshotQueryHandler"/> — the real data
/// behind <c>Trading.Application.Abstractions.IPortfolioSnapshotProvider</c>.
/// </summary>
public sealed class GetPortfolioSnapshotQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_account_with_no_fills_yet_reports_the_configured_baseline()
    {
        var handler = CreateHandler(account: null, openPositions: [], markPrice: null, dailyRealizedPnl: 0m);

        var result = await handler.HandleAsync(new GetPortfolioSnapshotQuery(Guid.NewGuid(), "BTCUSDT"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Equity.ShouldBe(10_000m);
        result.Value.AvailableBalance.ShouldBe(10_000m);
        result.Value.CurrentExposure.ShouldBe(0m);
        result.Value.OpenPositionCount.ShouldBe(0);
    }

    [Fact]
    public async Task Exposure_and_equity_mark_every_open_position_at_the_live_price()
    {
        var accountId = TradingAccountId.New();
        var account = PortfolioAccount.Create(accountId, 5_000m, "USDT", Now);

        var position = Position.Create(accountId, Symbol.Create("BTCUSDT"), "USDT", Now);
        position.ApplyFill(OrderSide.Buy, Quantity.Create(1m), Price.Create(50_000m), 0m, "USDT", "USDT", Now, Now);

        var handler = CreateHandler(account, [position], markPrice: 55_000m, dailyRealizedPnl: 0m);

        var result = await handler.HandleAsync(new GetPortfolioSnapshotQuery(accountId.Value, "BTCUSDT"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CurrentExposure.ShouldBe(55_000m);
        result.Value.CurrentSymbolExposure.ShouldBe(55_000m);
        result.Value.Equity.ShouldBe(60_000m); // 5,000 cash + 55,000 marked position
        result.Value.AvailableBalance.ShouldBe(5_000m);
        result.Value.OpenPositionCount.ShouldBe(1);

        // Unrealised at the live mark: (55,000 - 50,000) * 1 = 5,000, plus no realised trades today.
        result.Value.DailyPnl.ShouldBe(5_000m);
    }

    [Fact]
    public async Task A_symbol_with_no_open_position_reports_zero_symbol_exposure_even_with_other_positions_open()
    {
        var accountId = TradingAccountId.New();
        var account = PortfolioAccount.Create(accountId, 5_000m, "USDT", Now);

        var position = Position.Create(accountId, Symbol.Create("BTCUSDT"), "USDT", Now);
        position.ApplyFill(OrderSide.Buy, Quantity.Create(1m), Price.Create(50_000m), 0m, "USDT", "USDT", Now, Now);

        var handler = CreateHandler(account, [position], markPrice: 50_000m, dailyRealizedPnl: 0m);

        var result = await handler.HandleAsync(new GetPortfolioSnapshotQuery(accountId.Value, "ETHUSDT"), CancellationToken.None);

        result.Value.CurrentSymbolExposure.ShouldBe(0m);
        result.Value.CurrentExposure.ShouldBe(50_000m);
    }

    private static GetPortfolioSnapshotQueryHandler CreateHandler(
        PortfolioAccount? account, IReadOnlyList<Position> openPositions, decimal? markPrice, decimal dailyRealizedPnl)
        => new(
            new FakeAccountRepository(account),
            new FakePositionRepository(openPositions),
            new FakeTradeRepository(dailyRealizedPnl),
            new FakeMarkPriceProvider(markPrice),
            Options.Create(new PortfolioOptions { StartingCashBalance = 10_000m, QuoteAsset = "USDT" }),
            new FakeClock());

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeAccountRepository(PortfolioAccount? account) : IPortfolioAccountRepository
    {
        public void Add(PortfolioAccount account) => throw new NotSupportedException();

        public Task<PortfolioAccount?> GetAsync(TradingAccountId tradingAccountId, CancellationToken cancellationToken)
            => Task.FromResult(account);
    }

    private sealed class FakePositionRepository(IReadOnlyList<Position> openPositions) : IPositionRepository
    {
        public void Add(Position position) => throw new NotSupportedException();

        public Task<Position?> GetAsync(TradingAccountId tradingAccountId, Symbol symbol, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Position>> ListOpenAsync(TradingAccountId tradingAccountId, CancellationToken cancellationToken)
            => Task.FromResult(openPositions);

        public Task<decimal> SumLifetimeRealizedPnlAsync(TradingAccountId tradingAccountId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeTradeRepository(decimal dailyRealizedPnl) : ITradeRepository
    {
        public void Add(Trade trade) => throw new NotSupportedException();

        public Task<decimal> SumRealizedPnlSinceAsync(
            TradingAccountId tradingAccountId, DateTimeOffset sinceUtc, CancellationToken cancellationToken)
            => Task.FromResult(dailyRealizedPnl);
    }

    private sealed class FakeMarkPriceProvider(decimal? markPrice) : IMarkPriceProvider
    {
        public Task<decimal?> GetMarkPriceAsync(string symbol, CancellationToken cancellationToken)
            => Task.FromResult(markPrice);
    }
}
