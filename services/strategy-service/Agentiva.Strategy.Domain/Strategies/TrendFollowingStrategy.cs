using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Strategy.Domain.Strategies;

/// <summary>
/// Rides an established trend: price and the medium EMA both on the same
/// side of the long EMA, with a wide ATR stop because trend-following means
/// accepting larger swings in exchange for the big moves.
/// </summary>
public sealed class TrendFollowingStrategy : IStrategy
{
    private const int MediumPeriod = 50;
    private const int LongPeriod = 200;
    private const decimal StopAtrMultiple = 2.0m;
    private const decimal TakeAtrMultiple = 3.0m;

    public string Name => "TREND_FOLLOWING";

    public string Version => "1.0.0";

    public int MinimumBars => LongPeriod;

    public StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
    {
        var bars = context.Bars;
        var entryPrice = bars[^1].Close;

        if (bars.Count < MinimumBars)
        {
            return StrategyEvaluationResult.Hold(entryPrice, "INSUFFICIENT_DATA");
        }

        var ema50 = context.Indicators.Ema50;
        var ema200 = context.Indicators.Ema200;
        var rsi = context.Indicators.Rsi14;
        var atr = context.Indicators.Atr14;

        if (ema50 is null || ema200 is null || rsi is null || atr is null)
        {
            return StrategyEvaluationResult.Hold(entryPrice, "INSUFFICIENT_DATA");
        }

        var uptrend = entryPrice > ema50 && ema50 > ema200;
        var downtrend = entryPrice < ema50 && ema50 < ema200;

        // Separation between the medium and long EMA, relative to price, as a
        // proxy for how established the trend already is.
        var separationFraction = Math.Abs(ema50.Value - ema200.Value) / entryPrice;

        if (uptrend)
        {
            // An uptrend that is already extremely overbought is the one this
            // strategy should not chase — the next move is more likely a
            // pullback than a continuation.
            if (rsi >= 85m)
            {
                return StrategyEvaluationResult.Hold(entryPrice, "UPTREND", "FILTERED_EXTREME_RSI");
            }

            var (stop, take) = AtrTargets.For(TradeAction.Buy, entryPrice, atr.Value, StopAtrMultiple, TakeAtrMultiple);
            return new StrategyEvaluationResult(
                TradeAction.Buy, AtrTargets.Confidence(0.5m + separationFraction * 8m), entryPrice, stop, take,
                ["UPTREND_EMA50_ABOVE_EMA200"]);
        }

        if (downtrend)
        {
            if (rsi <= 15m)
            {
                return StrategyEvaluationResult.Hold(entryPrice, "DOWNTREND", "FILTERED_EXTREME_RSI");
            }

            var (stop, take) = AtrTargets.For(TradeAction.Sell, entryPrice, atr.Value, StopAtrMultiple, TakeAtrMultiple);
            return new StrategyEvaluationResult(
                TradeAction.Sell, AtrTargets.Confidence(0.5m + separationFraction * 8m), entryPrice, stop, take,
                ["DOWNTREND_EMA50_BELOW_EMA200"]);
        }

        return StrategyEvaluationResult.Hold(entryPrice, "NO_CLEAR_TREND");
    }
}
