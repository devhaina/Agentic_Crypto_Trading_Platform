using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Backtesting.Domain.Metrics;

namespace Agentiva.Backtesting.Domain.Runs;

/// <summary>
/// One historical simulation of one deterministic strategy against one
/// symbol, timeframe and period.
/// </summary>
/// <remarks>
/// Holds no exchange credential and places no order — see
/// <c>Agentiva.Backtesting.Domain.Simulation.BacktestSimulator</c>, the pure
/// function that actually produces the trades and metrics this aggregate
/// records. A run is created <see cref="BacktestStatus.Pending"/>, and this
/// aggregate's only job after that is to record the one outcome the
/// simulator reports — <see cref="Complete"/> or <see cref="Fail"/> — exactly
/// once.
/// </remarks>
public sealed class BacktestRun : AggregateRoot<BacktestId>
{
    private readonly List<BacktestTrade> _trades = [];

    private BacktestRun()
    {
    }

    private BacktestRun(
        BacktestId id,
        Symbol symbol,
        string timeframe,
        string strategyName,
        string strategyVersion,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        decimal startingCapital,
        Percentage feePercent,
        Percentage slippagePercent,
        int walkForwardWindowCount,
        DateTimeOffset createdAt)
        : base(id)
    {
        Symbol = symbol;
        Timeframe = timeframe;
        StrategyName = strategyName;
        StrategyVersion = strategyVersion;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        StartingCapital = startingCapital;
        FeePercent = feePercent;
        SlippagePercent = slippagePercent;
        WalkForwardWindowCount = walkForwardWindowCount;
        Status = BacktestStatus.Pending;
        CreatedAt = createdAt;
    }

    public Symbol Symbol { get; private set; }

    public string Timeframe { get; private set; } = string.Empty;

    /// <summary>Matches one <c>IStrategy.Name</c> in <c>Agentiva.BuildingBlocks.TradingRules</c>, e.g. <c>EMA_RSI</c>.</summary>
    public string StrategyName { get; private set; } = string.Empty;

    public string StrategyVersion { get; private set; } = string.Empty;

    /// <summary>Inclusive start of the historical window simulated.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    /// <summary>Exclusive end of the historical window simulated.</summary>
    public DateTimeOffset PeriodEnd { get; private set; }

    public decimal StartingCapital { get; private set; }

    /// <summary>Taker fee assumed on every simulated fill, as a fraction of notional.</summary>
    public Percentage FeePercent { get; private set; }

    /// <summary>Adverse price movement assumed on every simulated fill.</summary>
    public Percentage SlippagePercent { get; private set; }

    /// <summary>How many sequential, non-overlapping windows the full period is sliced into for validation.</summary>
    public int WalkForwardWindowCount { get; private set; }

    public BacktestStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Metrics over the whole period. Null until <see cref="Status"/> is <see cref="BacktestStatus.Completed"/>.</summary>
    public PerformanceMetrics? Metrics { get; private set; }

    public IReadOnlyList<BacktestTrade> Trades => _trades;

    /// <summary>Per-window metrics. A value-object blob assigned once by <see cref="Complete"/>, not a tracked collection.</summary>
    public IReadOnlyList<WalkForwardWindow> WalkForwardWindows { get; private set; } = [];

    /// <summary>Creates a new, not-yet-run backtest.</summary>
    /// <exception cref="DomainException">A parameter is out of range.</exception>
    public static BacktestRun Create(
        Symbol symbol,
        string timeframe,
        string strategyName,
        string strategyVersion,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        decimal startingCapital,
        Percentage feePercent,
        Percentage slippagePercent,
        int walkForwardWindowCount,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(timeframe))
        {
            throw new DomainException("backtest.timeframe.empty", "Timeframe must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(strategyName))
        {
            throw new DomainException("backtest.strategy_name.empty", "Strategy name must not be empty.");
        }

        if (periodEnd <= periodStart)
        {
            throw new DomainException(
                "backtest.period.invalid",
                $"Period end ({periodEnd:O}) must be after period start ({periodStart:O}).");
        }

        if (startingCapital <= 0m)
        {
            throw new DomainException(
                "backtest.starting_capital.non_positive",
                $"Starting capital must be positive but was {startingCapital}.");
        }

        if (walkForwardWindowCount < 1)
        {
            throw new DomainException(
                "backtest.walk_forward_windows.invalid",
                $"Walk-forward window count must be at least 1 but was {walkForwardWindowCount}.");
        }

        return new BacktestRun(
            BacktestId.New(), symbol, timeframe, strategyName, strategyVersion, periodStart, periodEnd,
            startingCapital, feePercent, slippagePercent, walkForwardWindowCount, createdAt);
    }

    /// <summary>Marks the run as actively simulating.</summary>
    /// <exception cref="DomainException">The run is not <see cref="BacktestStatus.Pending"/>.</exception>
    public void Start()
    {
        RequireStatus(BacktestStatus.Pending, nameof(Start));
        Status = BacktestStatus.Running;
    }

    /// <summary>
    /// Records a successful simulation's outcome. Exactly one of
    /// <see cref="Complete"/> or <see cref="Fail"/> is called once per run.
    /// </summary>
    /// <exception cref="DomainException">The run is not <see cref="BacktestStatus.Running"/>.</exception>
    public void Complete(
        IReadOnlyList<BacktestTrade> trades,
        PerformanceMetrics metrics,
        IReadOnlyList<WalkForwardWindow> walkForwardWindows,
        DateTimeOffset completedAt)
    {
        RequireStatus(BacktestStatus.Running, nameof(Complete));

        _trades.Clear();
        _trades.AddRange(trades);

        WalkForwardWindows = walkForwardWindows;
        Metrics = metrics;
        Status = BacktestStatus.Completed;
        CompletedAt = completedAt;
    }

    /// <summary>
    /// Records that the simulation could not be completed — missing
    /// historical data is the expected case, not a bug: an honest failure the
    /// caller can see, never a fabricated result.
    /// </summary>
    /// <exception cref="DomainException">The run is not <see cref="BacktestStatus.Running"/>.</exception>
    public void Fail(string reason, DateTimeOffset completedAt)
    {
        RequireStatus(BacktestStatus.Running, nameof(Fail));

        FailureReason = reason;
        Status = BacktestStatus.Failed;
        CompletedAt = completedAt;
    }

    private void RequireStatus(BacktestStatus required, string operation)
    {
        if (Status != required)
        {
            throw new DomainException(
                "backtest.invalid_transition",
                $"{operation} requires status {required} but the run is {Status}.");
        }
    }
}
