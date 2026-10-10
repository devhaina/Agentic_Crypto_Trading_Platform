using Agentiva.Backtesting.Application.Abstractions;
using Agentiva.Backtesting.Application.Contracts;
using Agentiva.Backtesting.Domain.Metrics;
using Agentiva.Backtesting.Domain.Runs;
using Agentiva.Backtesting.Domain.Simulation;
using Agentiva.Backtesting.Domain.Strategies;
using Agentiva.BuildingBlocks.Application.Abstractions;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Abstractions;
using Agentiva.BuildingBlocks.Common.Results;
using Agentiva.BuildingBlocks.Domain.Primitives;
using FluentValidation;

namespace Agentiva.Backtesting.Application.Commands;

/// <summary>Runs one historical simulation and persists its outcome.</summary>
/// <remarks>
/// Runs synchronously within the request: the simulation is a bounded CPU
/// computation over data already in the database, not a call to anything
/// external, and the strategies this service replays are cheap per bar (see
/// <c>BacktestSimulator</c>'s own remarks on cost). A period long enough to
/// make that a problem is a scope decision for a later phase, not a reason
/// to build a job queue for this one — see docs/architecture/known-limitations.md.
/// </remarks>
public sealed record RunBacktestCommand(
    string Symbol,
    string Timeframe,
    string StrategyName,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal StartingCapital,
    decimal FeePercent,
    decimal SlippagePercent,
    decimal RiskPerTradePercent,
    int WalkForwardWindowCount,
    int CandleBufferCapacity)
    : ICommand<Result<BacktestResultDto>>;

/// <summary>Structural validation only — an unknown strategy or symbol is a runtime, not a shape, failure.</summary>
public sealed class RunBacktestCommandValidator : AbstractValidator<RunBacktestCommand>
{
    public RunBacktestCommandValidator()
    {
        RuleFor(c => c.Symbol).NotEmpty().MaximumLength(24);
        RuleFor(c => c.Timeframe).NotEmpty().MaximumLength(8);
        RuleFor(c => c.StrategyName).NotEmpty().MaximumLength(50);
        RuleFor(c => c.PeriodEnd).GreaterThan(c => c.PeriodStart);
        RuleFor(c => c.StartingCapital).GreaterThan(0m);
        RuleFor(c => c.FeePercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.SlippagePercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.RiskPerTradePercent).InclusiveBetween(0m, 100m);
        RuleFor(c => c.WalkForwardWindowCount).InclusiveBetween(1, 52);
        RuleFor(c => c.CandleBufferCapacity).InclusiveBetween(50, 2000);
    }
}

/// <summary>Handles <see cref="RunBacktestCommand"/>.</summary>
public sealed class RunBacktestCommandHandler(
    IBacktestRunRepository runs, IHistoricalCandleReader candles, IUnitOfWork unitOfWork, IClock clock)
    : IRequestHandler<RunBacktestCommand, Result<BacktestResultDto>>
{
    public async Task<Result<BacktestResultDto>> HandleAsync(RunBacktestCommand command, CancellationToken cancellationToken)
    {
        var strategy = StrategyCatalog.TryResolve(command.StrategyName);

        if (strategy is null)
        {
            return Error.Validation(
                "backtest.strategy.unknown",
                $"'{command.StrategyName}' is not a known strategy. Known strategies: "
                + $"{string.Join(", ", StrategyCatalog.KnownStrategyNames)}.");
        }

        if (!Symbol.TryCreate(command.Symbol, out var symbol))
        {
            return Error.Validation("backtest.symbol.invalid", $"'{command.Symbol}' is not a valid symbol.");
        }

        var createdAt = clock.UtcNow;
        var feePercent = Percentage.FromPercent(command.FeePercent);
        var slippagePercent = Percentage.FromPercent(command.SlippagePercent);

        var run = BacktestRun.Create(
            symbol, command.Timeframe, strategy.Name, strategy.Version, command.PeriodStart, command.PeriodEnd,
            command.StartingCapital, feePercent, slippagePercent, command.WalkForwardWindowCount, createdAt);

        run.Start();

        var bars = await candles.GetCandlesAsync(
            symbol.Value, command.Timeframe, command.PeriodStart, command.PeriodEnd, cancellationToken);

        if (bars.Count < strategy.MinimumBars)
        {
            run.Fail(
                $"Only {bars.Count} historical candle(s) are available for {symbol.Value} {command.Timeframe} "
                + $"in this period; {strategy.Name} needs at least {strategy.MinimumBars}.",
                clock.UtcNow);
        }
        else
        {
            var output = BacktestSimulator.Run(
                strategy, bars, run.Id, symbol.Value, command.Timeframe, command.CandleBufferCapacity,
                command.StartingCapital, feePercent, slippagePercent,
                Percentage.FromPercent(command.RiskPerTradePercent));

            var metrics = PerformanceCalculator.Calculate(
                output.Trades, output.EquityCurve, command.StartingCapital, command.Timeframe);

            var windows = PerformanceCalculator.SplitIntoWindows(
                output.Trades, output.EquityCurve, command.StartingCapital, command.PeriodStart, command.PeriodEnd,
                command.WalkForwardWindowCount, command.Timeframe);

            run.Complete(output.Trades, metrics, windows, clock.UtcNow);
        }

        runs.Add(run);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BacktestMapper.ToResultDto(run));
    }
}
