using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Strategy.Domain.Indicators;

namespace Agentiva.Strategy.Domain.Strategies;

/// <summary>Everything one strategy evaluation needs: the closed bars and their indicators.</summary>
/// <param name="Symbol">Trading pair the bars belong to.</param>
/// <param name="Timeframe">Candle interval the bars were closed on.</param>
/// <param name="Bars">Closed bars, oldest first, newest last.</param>
/// <param name="Indicators">Indicator values computed over <paramref name="Bars"/>.</param>
public sealed record StrategyEvaluationContext(
    string Symbol,
    string Timeframe,
    IReadOnlyList<PriceBar> Bars,
    IndicatorSet Indicators);

/// <summary>A strategy's deterministic decision for one evaluation.</summary>
/// <param name="Action">Buy, sell, or explicitly stand aside.</param>
/// <param name="Confidence">How strongly the data supports <paramref name="Action"/>.</param>
/// <param name="EntryPrice">The price the decision was made against — the latest close.</param>
/// <param name="StopLoss">Protective stop. Null only when <paramref name="Action"/> is <see cref="TradeAction.Hold"/>.</param>
/// <param name="TakeProfit">Target. Null only when <paramref name="Action"/> is <see cref="TradeAction.Hold"/>.</param>
/// <param name="ReasonCodes">Stable codes explaining the decision, carried through to the audit record.</param>
public sealed record StrategyEvaluationResult(
    TradeAction Action,
    Percentage Confidence,
    decimal EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    IReadOnlyList<string> ReasonCodes)
{
    public static StrategyEvaluationResult Hold(decimal entryPrice, params string[] reasonCodes)
        => new(TradeAction.Hold, Percentage.Zero, entryPrice, null, null, reasonCodes);
}

/// <summary>
/// A deterministic strategy: closed bars and their indicators in, one
/// Buy/Sell/Hold decision out. No randomness, no model call, no I/O.
/// </summary>
public interface IStrategy
{
    /// <summary>Stable strategy name, e.g. <c>EMA_RSI</c>. Part of the published signal contract.</summary>
    string Name { get; }

    /// <summary>Semantic version of this strategy's logic. Bump on any change to the decision rules.</summary>
    string Version { get; }

    /// <summary>Bars required before this strategy can produce anything but an insufficient-data hold.</summary>
    int MinimumBars { get; }

    /// <summary>Evaluates the context and returns a decision. Never throws on insufficient data.</summary>
    StrategyEvaluationResult Evaluate(StrategyEvaluationContext context);
}
