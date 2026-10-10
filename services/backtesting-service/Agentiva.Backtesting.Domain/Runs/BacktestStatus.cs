namespace Agentiva.Backtesting.Domain.Runs;

/// <summary>Lifecycle state of one backtest run.</summary>
public enum BacktestStatus
{
    /// <summary>Accepted but not yet simulated.</summary>
    Pending = 1,

    /// <summary>The simulation is currently running.</summary>
    Running = 2,

    Completed = 3,
    Failed = 4
}

/// <summary>Why a simulated position was closed.</summary>
public enum ExitReason
{
    /// <summary>Price touched the strategy's own protective stop.</summary>
    StopLoss = 1,

    /// <summary>Price touched the strategy's own target.</summary>
    TakeProfit = 2,

    /// <summary>The historical data ran out while the position was still open.</summary>
    EndOfData = 3
}
