using Agentiva.BuildingBlocks.TradingRules.Indicators;

namespace Agentiva.Strategy.Infrastructure.Candles;

/// <summary>
/// A fixed-capacity rolling window of the most recently closed bars for one
/// (symbol, timeframe) pair.
/// </summary>
/// <remarks>
/// <para>
/// Built up purely from consumed <c>market.candle.created</c> events — this
/// service makes no call back to the Market Data Service to warm itself from
/// history. The practical effect, documented in
/// docs/architecture/known-limitations.md, is that every strategy holds at
/// "insufficient data" until enough live candles have arrived after this
/// service starts; the trend-following strategy alone needs 200 of them.
/// </para>
/// <para>
/// Not thread-safe by itself. <see cref="CandleBufferStore"/> is the
/// synchronisation boundary: it hands out one buffer per key and the event
/// consumer processes one message at a time per queue, so concurrent access
/// to the same buffer is not expected in practice, but the store still guards
/// buffer creation with a lock since RabbitMQ consumer dispatch is
/// callback-driven.
/// </para>
/// </remarks>
public sealed class CandleBuffer(int capacity)
{
    private readonly List<PriceBar> _bars = new(capacity);

    public int Count => _bars.Count;

    /// <summary>Appends a closed bar, evicting the oldest once at capacity.</summary>
    /// <remarks>
    /// A replayed candle (the same open time arriving twice — a RabbitMQ
    /// redelivery, or Binance resending the last bar after a reconnect) is
    /// updated in place rather than appended twice: duplicating it would
    /// silently corrupt every indicator computed over this window.
    /// </remarks>
    public void Append(PriceBar bar)
    {
        var existingIndex = _bars.FindIndex(b => b.OpenTime == bar.OpenTime);

        if (existingIndex >= 0)
        {
            _bars[existingIndex] = bar;
            return;
        }

        _bars.Add(bar);

        if (_bars.Count > capacity)
        {
            _bars.RemoveAt(0);
        }
    }

    /// <summary>A snapshot of the current window, oldest first.</summary>
    public IReadOnlyList<PriceBar> Snapshot() => _bars.ToArray();
}
