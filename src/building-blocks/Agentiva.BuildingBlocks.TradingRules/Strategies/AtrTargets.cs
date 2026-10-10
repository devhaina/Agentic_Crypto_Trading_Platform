using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.BuildingBlocks.TradingRules.Strategies;

/// <summary>Shared ATR-based stop-loss/take-profit sizing used by every strategy here.</summary>
/// <remarks>
/// A fixed price-distance stop would be the wrong width in both a calm and a
/// volatile market; sizing it off ATR means the stop widens and narrows with
/// the symbol's own recently observed volatility instead of a number picked
/// once and never revisited.
/// </remarks>
internal static class AtrTargets
{
    /// <summary>
    /// Returns (stopLoss, takeProfit) for a long or short entry, placed
    /// <paramref name="stopAtrMultiple"/> and <paramref name="takeAtrMultiple"/>
    /// average true ranges away from <paramref name="entryPrice"/>.
    /// </summary>
    public static (decimal StopLoss, decimal TakeProfit) For(
        TradeAction action, decimal entryPrice, decimal atr, decimal stopAtrMultiple, decimal takeAtrMultiple)
    {
        var stopDistance = atr * stopAtrMultiple;
        var takeDistance = atr * takeAtrMultiple;

        return action == TradeAction.Buy
            ? (entryPrice - stopDistance, entryPrice + takeDistance)
            : (entryPrice + stopDistance, entryPrice - takeDistance);
    }

    /// <summary>Clamps a confidence fraction to a sane range: never absolute certainty, never zero for an actionable signal.</summary>
    public static Percentage Confidence(decimal fraction) => Percentage.FromFraction(Math.Clamp(fraction, 0.5m, 0.95m));
}
