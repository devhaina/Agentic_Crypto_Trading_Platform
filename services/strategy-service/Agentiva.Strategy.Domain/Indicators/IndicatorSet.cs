namespace Agentiva.Strategy.Domain.Indicators;

/// <summary>
/// Every indicator value computed for one evaluation, keyed the same way
/// across the engine, the <c>indicator_snapshots</c> hypertable and the AI
/// platform's <c>get_indicators</c> tool.
/// </summary>
public sealed record IndicatorSet
{
    public decimal? Ema12 { get; init; }

    public decimal? Ema26 { get; init; }

    public decimal? Ema50 { get; init; }

    public decimal? Ema200 { get; init; }

    public decimal? Rsi14 { get; init; }

    public decimal? Macd { get; init; }

    public decimal? MacdSignal { get; init; }

    public decimal? MacdHistogram { get; init; }

    public decimal? Atr14 { get; init; }

    public decimal? Vwap { get; init; }

    /// <summary>Computes every indicator this engine knows about over the given bars.</summary>
    public static IndicatorSet Compute(IReadOnlyList<PriceBar> bars)
    {
        var closes = bars.Select(b => b.Close).ToArray();
        var macd = IndicatorEngine.Macd(closes);

        return new IndicatorSet
        {
            Ema12 = IndicatorEngine.Ema(closes, 12),
            Ema26 = IndicatorEngine.Ema(closes, 26),
            Ema50 = IndicatorEngine.Ema(closes, 50),
            Ema200 = IndicatorEngine.Ema(closes, 200),
            Rsi14 = IndicatorEngine.Rsi(closes),
            Macd = macd?.Macd,
            MacdSignal = macd?.Signal,
            MacdHistogram = macd?.Histogram,
            Atr14 = IndicatorEngine.Atr(bars),
            Vwap = IndicatorEngine.Vwap(bars)
        };
    }

    /// <summary>
    /// A name/value map of every indicator that was actually computable,
    /// suitable for the <c>indicator_snapshots.indicators</c> jsonb column and
    /// for the <c>get_indicators</c> tool response. Values are kept as strings —
    /// see the column's own remarks on why a jsonb number is the wrong type for
    /// a decimal price.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToDictionary()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string name, decimal? value)
        {
            if (value is not null)
            {
                map[name] = value.Value.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        Add("EMA_12", Ema12);
        Add("EMA_26", Ema26);
        Add("EMA_50", Ema50);
        Add("EMA_200", Ema200);
        Add("RSI_14", Rsi14);
        Add("MACD", Macd);
        Add("MACD_SIGNAL", MacdSignal);
        Add("MACD_HISTOGRAM", MacdHistogram);
        Add("ATR_14", Atr14);
        Add("VWAP", Vwap);

        return map;
    }
}
