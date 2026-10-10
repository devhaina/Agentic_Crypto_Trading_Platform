using Agentiva.BuildingBlocks.TradingRules.Strategies;

namespace Agentiva.Backtesting.Domain.Strategies;

/// <summary>
/// Resolves a strategy name to the exact <see cref="IStrategy"/> instance
/// the Strategy Service runs live.
/// </summary>
/// <remarks>
/// Deliberately not a lookup against the Strategy Service's own
/// <c>StrategyDefinition</c> table — that would be the cross-service
/// reference Phase 8 extracted this logic to avoid. A strategy name here is
/// resolved straight to code, the same way <c>StrategyDefinition</c>'s own
/// remarks describe the decision logic as living "entirely in code".
/// </remarks>
public static class StrategyCatalog
{
    private static readonly IReadOnlyDictionary<string, Func<IStrategy>> Factories =
        new Dictionary<string, Func<IStrategy>>(StringComparer.OrdinalIgnoreCase)
        {
            ["EMA_RSI"] = () => new EmaRsiStrategy(),
            ["BREAKOUT"] = () => new BreakoutStrategy(),
            ["TREND_FOLLOWING"] = () => new TrendFollowingStrategy()
        };

    /// <summary>Every strategy name a backtest can be run against.</summary>
    public static IReadOnlyCollection<string> KnownStrategyNames => (IReadOnlyCollection<string>)Factories.Keys;

    /// <summary>Resolves a strategy name, or <c>null</c> when it is not recognised.</summary>
    public static IStrategy? TryResolve(string strategyName)
        => Factories.TryGetValue(strategyName, out var factory) ? factory() : null;
}
