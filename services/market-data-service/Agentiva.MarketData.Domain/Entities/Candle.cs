using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.MarketData.Domain.Primitives;

namespace Agentiva.MarketData.Domain.Entities;

/// <summary>A completed OHLCV candle.</summary>
/// <remarks>
/// Constructed only from a kline event whose <c>x</c> (is-final) flag is true.
/// An in-progress candle is never modelled here: a strategy that acted on one
/// would be using look-ahead information in live trading, since the bar can
/// still change, and the easiest way to produce a backtest that cannot be
/// reproduced in production.
/// </remarks>
public sealed record Candle
{
    private Candle(
        Symbol symbol,
        Timeframe timeframe,
        DateTimeOffset openTime,
        DateTimeOffset closeTime,
        Price open,
        Price high,
        Price low,
        Price close,
        decimal volume,
        decimal quoteVolume,
        int tradeCount,
        string exchange)
    {
        Symbol = symbol;
        Timeframe = timeframe;
        OpenTime = openTime;
        CloseTime = closeTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        QuoteVolume = quoteVolume;
        TradeCount = tradeCount;
        Exchange = exchange;
    }

    public Symbol Symbol { get; }

    public Timeframe Timeframe { get; }

    public DateTimeOffset OpenTime { get; }

    public DateTimeOffset CloseTime { get; }

    public Price Open { get; }

    public Price High { get; }

    public Price Low { get; }

    public Price Close { get; }

    public decimal Volume { get; }

    public decimal QuoteVolume { get; }

    public int TradeCount { get; }

    public string Exchange { get; }

    /// <summary>Builds a closed candle, rejecting an internally inconsistent OHLC range.</summary>
    /// <exception cref="DomainException">
    /// A price is not strictly positive, a volume is negative, or high/low are not the
    /// extremes of open/high/low/close — the same invariant enforced by the
    /// <c>ck_market_candles_ohlc</c> constraint on <c>market_candles</c>, checked here first so
    /// a malformed exchange payload never reaches the database.
    /// </exception>
    public static Candle Create(
        string symbol,
        string timeframe,
        DateTimeOffset openTime,
        DateTimeOffset closeTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume,
        decimal quoteVolume,
        int tradeCount,
        string exchange)
    {
        if (!(high >= low && high >= open && high >= close && low <= open && low <= close))
        {
            throw new DomainException(
                "domain.candle.invalid_ohlc",
                $"Candle for {symbol} at {openTime:O} has an inconsistent OHLC range: "
                + $"open={open}, high={high}, low={low}, close={close}.");
        }

        if (volume < 0m || quoteVolume < 0m)
        {
            throw new DomainException(
                "domain.candle.negative_volume",
                $"Candle for {symbol} at {openTime:O} has a negative volume.");
        }

        if (tradeCount < 0)
        {
            throw new DomainException(
                "domain.candle.negative_trade_count",
                $"Candle for {symbol} at {openTime:O} has a negative trade count.");
        }

        return new Candle(
            Symbol.Create(symbol),
            Timeframe.Create(timeframe),
            openTime,
            closeTime,
            Price.Create(open),
            Price.Create(high),
            Price.Create(low),
            Price.Create(close),
            volume,
            quoteVolume,
            tradeCount,
            exchange);
    }
}
