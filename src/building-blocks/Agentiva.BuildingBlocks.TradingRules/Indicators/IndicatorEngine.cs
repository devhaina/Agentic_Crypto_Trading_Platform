namespace Agentiva.BuildingBlocks.TradingRules.Indicators;

/// <summary>
/// The deterministic indicator engine: EMA, RSI, MACD, ATR and VWAP over a
/// series of closed bars.
/// </summary>
/// <remarks>
/// <para>
/// Every function returns <c>null</c> rather than an approximate value when
/// the series is too short to compute the indicator meaningfully. A wrong
/// number that merely looks plausible is far more dangerous here than an
/// honest "not enough data" — it is exactly the kind of error a strategy
/// cannot tell apart from a real signal.
/// </para>
/// <para>
/// Pure functions over a list a caller already holds: this type owns no
/// state of its own. The rolling window that the list comes from is an
/// infrastructure concern (see <c>CandleBuffer</c>), not a domain one.
/// </para>
/// </remarks>
public static class IndicatorEngine
{
    /// <summary>Exponential moving average of <paramref name="closes"/> over <paramref name="period"/> bars.</summary>
    public static decimal? Ema(IReadOnlyList<decimal> closes, int period)
    {
        var series = EmaSeries(closes, period);
        return series is { Length: > 0 } ? series[^1] : null;
    }

    /// <summary>
    /// Wilder's relative strength index. Returns a value in [0, 100], or
    /// <c>null</c> when there are fewer than <paramref name="period"/> + 1 closes.
    /// </summary>
    public static decimal? Rsi(IReadOnlyList<decimal> closes, int period = 14)
    {
        if (closes.Count < period + 1)
        {
            return null;
        }

        decimal gainSum = 0m, lossSum = 0m;

        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0)
            {
                gainSum += change;
            }
            else
            {
                lossSum += -change;
            }
        }

        var avgGain = gainSum / period;
        var avgLoss = lossSum / period;

        for (var i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? -change : 0m;

            // Wilder smoothing: each new bar carries 1/period of the weight,
            // the same recurrence ATR uses below.
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0m)
        {
            // No losses at all in the window: maximally overbought, not undefined.
            return 100m;
        }

        var relativeStrength = avgGain / avgLoss;
        return 100m - 100m / (1m + relativeStrength);
    }

    /// <summary>
    /// MACD line (EMA12 − EMA26), its signal line (EMA9 of the MACD line) and
    /// their histogram. <c>null</c> when there are fewer than 35 closes — 26
    /// to seed the slow EMA plus 9 more to seed the signal line.
    /// </summary>
    public static (decimal Macd, decimal Signal, decimal Histogram)? Macd(
        IReadOnlyList<decimal> closes, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        var fast = EmaSeries(closes, fastPeriod);
        var slow = EmaSeries(closes, slowPeriod);

        if (fast is null || slow is null || slow.Length < signalPeriod)
        {
            return null;
        }

        // The fast series starts earlier than the slow one; align both to the
        // slow series' length so each element compares the same bar.
        var offset = fast.Length - slow.Length;
        var macdLine = new decimal[slow.Length];

        for (var i = 0; i < slow.Length; i++)
        {
            macdLine[i] = fast[i + offset] - slow[i];
        }

        var signalSeries = EmaSeries(macdLine, signalPeriod);

        if (signalSeries is null)
        {
            return null;
        }

        var macd = macdLine[^1];
        var signal = signalSeries[^1];

        return (macd, signal, macd - signal);
    }

    /// <summary>
    /// Wilder's average true range. <c>null</c> when there are fewer than
    /// <paramref name="period"/> + 1 bars.
    /// </summary>
    public static decimal? Atr(IReadOnlyList<PriceBar> bars, int period = 14)
    {
        if (bars.Count < period + 1)
        {
            return null;
        }

        decimal atr = 0m;

        for (var i = 1; i <= period; i++)
        {
            atr += TrueRange(bars[i], bars[i - 1]);
        }

        atr /= period;

        for (var i = period + 1; i < bars.Count; i++)
        {
            var trueRange = TrueRange(bars[i], bars[i - 1]);
            atr = (atr * (period - 1) + trueRange) / period;
        }

        return atr;
    }

    /// <summary>
    /// Volume-weighted average price over every bar supplied.
    /// </summary>
    /// <remarks>
    /// A true VWAP resets at the start of a trading session; this service
    /// tracks no session boundary, so this is computed over whatever window
    /// the caller's buffer currently holds — documented in
    /// docs/architecture/known-limitations.md as a deliberate simplification,
    /// not a session VWAP.
    /// </remarks>
    public static decimal? Vwap(IReadOnlyList<PriceBar> bars)
    {
        if (bars.Count == 0)
        {
            return null;
        }

        decimal priceVolumeSum = 0m, volumeSum = 0m;

        foreach (var bar in bars)
        {
            var typicalPrice = (bar.High + bar.Low + bar.Close) / 3m;
            priceVolumeSum += typicalPrice * bar.Volume;
            volumeSum += bar.Volume;
        }

        return volumeSum == 0m ? null : priceVolumeSum / volumeSum;
    }

    /// <summary>
    /// Annualises a per-bar ATR into a rough volatility percentage: the ATR as
    /// a fraction of price, scaled by the square root of how many such bars
    /// make up a year — the standard way to compare a measurement taken at one
    /// sampling frequency against an annual figure.
    /// </summary>
    /// <remarks>
    /// A proxy, not a GARCH model: ATR mixes trend and noise, and this treats
    /// every symbol's recent range as equally representative of its future
    /// one. Good enough to replace "no estimate at all" with a real, moving
    /// number; not good enough to be the only volatility input a production
    /// risk system ever uses. See docs/architecture/known-limitations.md.
    /// </remarks>
    /// <returns><c>null</c> when the ATR is unavailable, the price is non-positive, or the timeframe is unrecognised.</returns>
    public static decimal? AnnualizedVolatilityPercent(decimal? atr, decimal price, string timeframe)
    {
        if (atr is null || price <= 0m)
        {
            return null;
        }

        var barsPerYear = TimeframeCalendar.BarsPerYear(timeframe);

        if (barsPerYear is null)
        {
            return null;
        }

        return atr.Value / price * (decimal)Math.Sqrt(barsPerYear.Value) * 100m;
    }

    private static decimal TrueRange(PriceBar current, PriceBar previous)
    {
        var highLow = current.High - current.Low;
        var highPrevClose = Math.Abs(current.High - previous.Close);
        var lowPrevClose = Math.Abs(current.Low - previous.Close);

        return Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose));
    }

    /// <summary>
    /// The full EMA series aligned to the tail of <paramref name="values"/>,
    /// seeded with the simple moving average of the first <paramref name="period"/>
    /// values. <c>null</c> when there are fewer than <paramref name="period"/> values.
    /// </summary>
    private static decimal[]? EmaSeries(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count < period)
        {
            return null;
        }

        var multiplier = 2m / (period + 1);
        var series = new decimal[values.Count - period + 1];

        decimal sum = 0m;
        for (var i = 0; i < period; i++)
        {
            sum += values[i];
        }

        series[0] = sum / period;

        for (var i = period; i < values.Count; i++)
        {
            series[i - period + 1] = (values[i] - series[i - period]) * multiplier + series[i - period];
        }

        return series;
    }
}
