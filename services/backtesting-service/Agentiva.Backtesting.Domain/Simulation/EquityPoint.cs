using Agentiva.Backtesting.Domain.Runs;

namespace Agentiva.Backtesting.Domain.Simulation;

/// <summary>One mark-to-market account value, taken at the close of one simulated bar.</summary>
public sealed record EquityPoint(DateTimeOffset Time, decimal Equity);

/// <summary>What one simulation pass produced: the trades it closed, and the equity curve for scoring.</summary>
public sealed record SimulationOutput(IReadOnlyList<BacktestTrade> Trades, IReadOnlyList<EquityPoint> EquityCurve);
