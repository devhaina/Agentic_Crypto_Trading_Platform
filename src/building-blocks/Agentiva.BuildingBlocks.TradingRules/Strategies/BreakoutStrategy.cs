using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.BuildingBlocks.TradingRules.Strategies;

/// <summary>
/// Donchian-channel breakout: the close exceeds the high or low of the
/// preceding <see cref="ChannelLength"/> bars.
/// </summary>
/// <remarks>
/// The channel is computed over the bars <em>before</em> the current one,
/// never including it — a breakout is "this bar exceeded every recent bar
/// that came before it", not "this bar is part of its own new extreme",
/// which would be true of almost every bar in a trending market and signal
/// constantly.
/// </remarks>
public sealed class BreakoutStrategy : IStrategy
{
    private const int ChannelLength = 20;
    private const decimal StopAtrMultiple = 1.0m;
    private const decimal TakeAtrMultiple = 2.5m;

    public string Name => "BREAKOUT";

    public string Version => "1.0.0";

    public int MinimumBars => ChannelLength + 1;

    public StrategyEvaluationResult Evaluate(StrategyEvaluationContext context)
    {
        var bars = context.Bars;
        var entryPrice = bars[^1].Close;

        if (bars.Count < MinimumBars)
        {
            return StrategyEvaluationResult.Hold(entryPrice, "INSUFFICIENT_DATA");
        }

        var atr = context.Indicators.Atr14;

        if (atr is null)
        {
            return StrategyEvaluationResult.Hold(entryPrice, "INSUFFICIENT_DATA");
        }

        var channel = bars.Skip(bars.Count - ChannelLength - 1).Take(ChannelLength).ToArray();
        var highestHigh = channel.Max(b => b.High);
        var lowestLow = channel.Min(b => b.Low);

        if (entryPrice > highestHigh)
        {
            var breakoutFraction = (entryPrice - highestHigh) / entryPrice;
            var (stop, take) = AtrTargets.For(TradeAction.Buy, entryPrice, atr.Value, StopAtrMultiple, TakeAtrMultiple);
            return new StrategyEvaluationResult(
                TradeAction.Buy, AtrTargets.Confidence(0.5m + breakoutFraction * 20m), entryPrice, stop, take,
                ["BREAKOUT_ABOVE_DONCHIAN_HIGH"]);
        }

        if (entryPrice < lowestLow)
        {
            var breakoutFraction = (lowestLow - entryPrice) / entryPrice;
            var (stop, take) = AtrTargets.For(TradeAction.Sell, entryPrice, atr.Value, StopAtrMultiple, TakeAtrMultiple);
            return new StrategyEvaluationResult(
                TradeAction.Sell, AtrTargets.Confidence(0.5m + breakoutFraction * 20m), entryPrice, stop, take,
                ["BREAKOUT_BELOW_DONCHIAN_LOW"]);
        }

        return StrategyEvaluationResult.Hold(entryPrice, "WITHIN_CHANNEL");
    }
}
