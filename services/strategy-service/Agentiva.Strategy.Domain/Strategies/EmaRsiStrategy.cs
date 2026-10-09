using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Strategy.Domain.Indicators;

namespace Agentiva.Strategy.Domain.Strategies;

/// <summary>
/// EMA(12/26) crossover, filtered by RSI(14) so a cross deep into overbought
/// or oversold territory is not chased.
/// </summary>
/// <remarks>
/// A crossover is a transition, not a level: holding a static "fast EMA is
/// above slow EMA" state would re-signal on every bar for as long as the
/// condition holds. This strategy therefore recomputes the previous bar's
/// EMA values from <see cref="StrategyEvaluationContext.Bars"/> rather than
/// relying only on the current snapshot in <see cref="StrategyEvaluationContext.Indicators"/>,
/// so it can tell "just crossed" from "has been above for ten bars".
/// </remarks>
public sealed class EmaRsiStrategy : IStrategy
{
    private const int FastPeriod = 12;
    private const int SlowPeriod = 26;
    private const decimal StopAtrMultiple = 1.5m;
    private const decimal TakeAtrMultiple = 2.0m;

    public string Name => "EMA_RSI";

    public string Version => "1.0.0";

    public int MinimumBars => SlowPeriod + 1;

    public StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
    {
        var bars = context.Bars;
        var entryPrice = bars[^1].Close;

        if (bars.Count < MinimumBars)
        {
            return StrategyEvaluationResult.Hold(entryPrice, "INSUFFICIENT_DATA");
        }

        var closes = bars.Select(b => b.Close).ToArray();
        var previousCloses = closes[..^1];

        var fastNow = context.Indicators.Ema12;
        var slowNow = context.Indicators.Ema26;
        var fastPrev = IndicatorEngine.Ema(previousCloses, FastPeriod);
        var slowPrev = IndicatorEngine.Ema(previousCloses, SlowPeriod);
        var rsi = context.Indicators.Rsi14;
        var atr = context.Indicators.Atr14;

        if (fastNow is null || slowNow is null || fastPrev is null || slowPrev is null || rsi is null || atr is null)
        {
            return StrategyEvaluationResult.Hold(entryPrice, "INSUFFICIENT_DATA");
        }

        var crossedUp = fastPrev <= slowPrev && fastNow > slowNow;
        var crossedDown = fastPrev >= slowPrev && fastNow < slowNow;

        // The EMA gap relative to price, scaled into a confidence contribution.
        // A crossover that opens a wide gap immediately is a stronger signal
        // than one that barely ticks across.
        var gapFraction = Math.Abs(fastNow.Value - slowNow.Value) / entryPrice;

        if (crossedUp)
        {
            if (rsi >= 70m)
            {
                return StrategyEvaluationResult.Hold(entryPrice, "EMA_CROSS_UP", "FILTERED_OVERBOUGHT_RSI");
            }

            var (stop, take) = AtrTargets.For(TradeAction.Buy, entryPrice, atr.Value, StopAtrMultiple, TakeAtrMultiple);
            return new StrategyEvaluationResult(
                TradeAction.Buy, AtrTargets.Confidence(0.5m + gapFraction * 5m), entryPrice, stop, take,
                ["EMA_CROSS_UP"]);
        }

        if (crossedDown)
        {
            if (rsi <= 30m)
            {
                return StrategyEvaluationResult.Hold(entryPrice, "EMA_CROSS_DOWN", "FILTERED_OVERSOLD_RSI");
            }

            var (stop, take) = AtrTargets.For(TradeAction.Sell, entryPrice, atr.Value, StopAtrMultiple, TakeAtrMultiple);
            return new StrategyEvaluationResult(
                TradeAction.Sell, AtrTargets.Confidence(0.5m + gapFraction * 5m), entryPrice, stop, take,
                ["EMA_CROSS_DOWN"]);
        }

        return StrategyEvaluationResult.Hold(entryPrice, "NO_CROSSOVER");
    }
}
