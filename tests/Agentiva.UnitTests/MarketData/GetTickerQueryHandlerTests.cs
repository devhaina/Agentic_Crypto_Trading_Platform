using Agentiva.MarketData.Application.Abstractions;
using Agentiva.MarketData.Application.Contracts;
using Agentiva.MarketData.Application.Queries;
using Shouldly;
using Xunit;

namespace Agentiva.UnitTests.MarketData;

/// <summary>
/// Tests for <see cref="GetTickerQueryHandler"/>'s cache-first, repository-fallback behaviour.
/// </summary>
public sealed class GetTickerQueryHandlerTests
{
    private static readonly TickerDto SampleTicker = new(
        "BTCUSDT", 100m, 1m, 101m, 1m, 100.5m, DateTimeOffset.UtcNow, "binance");

    [Fact]
    public async Task Returns_the_cached_value_without_touching_the_repository()
    {
        var cache = new FakeCache { Ticker = SampleTicker };
        var repository = new FakeRepository { ThrowIfCalled = true };

        var result = await new GetTickerQueryHandler(cache, repository)
            .HandleAsync(new GetTickerQuery("BTCUSDT"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(SampleTicker);
    }

    [Fact]
    public async Task Falls_back_to_the_repository_on_a_cache_miss()
    {
        var cache = new FakeCache { Ticker = null };
        var repository = new FakeRepository { Ticker = SampleTicker };

        var result = await new GetTickerQueryHandler(cache, repository)
            .HandleAsync(new GetTickerQuery("BTCUSDT"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(SampleTicker);
    }

    [Fact]
    public async Task Fails_with_not_found_when_neither_source_has_data()
    {
        var cache = new FakeCache { Ticker = null };
        var repository = new FakeRepository { Ticker = null };

        var result = await new GetTickerQueryHandler(cache, repository)
            .HandleAsync(new GetTickerQuery("BTCUSDT"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("market_data.ticker_unavailable");
    }

    private sealed class FakeCache : IMarketDataCache
    {
        public TickerDto? Ticker { get; set; }

        public Task SetLatestTickerAsync(TickerDto ticker, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TickerDto?> GetLatestTickerAsync(string symbol, CancellationToken cancellationToken)
            => Task.FromResult(Ticker);

        public Task SetLatestCandleAsync(CandleDto candle, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CandleDto?> GetLatestCandleAsync(string symbol, string timeframe, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SetLatestOrderBookAsync(OrderBookDto orderBook, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OrderBookDto?> GetLatestOrderBookAsync(string symbol, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeRepository : IMarketDataReadRepository
    {
        public TickerDto? Ticker { get; set; }

        public bool ThrowIfCalled { get; set; }

        public Task<TickerDto?> GetLatestTickAsync(string symbol, CancellationToken cancellationToken)
        {
            if (ThrowIfCalled)
            {
                throw new InvalidOperationException(
                    "The repository must not be queried when the cache already has a value.");
            }

            return Task.FromResult(Ticker);
        }

        public Task<IReadOnlyList<CandleDto>> GetRecentCandlesAsync(
            string symbol, string timeframe, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<OrderBookDto?> GetLatestOrderBookAsync(string symbol, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IndicatorsDto?> GetLatestIndicatorsAsync(
            string symbol, string timeframe, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
