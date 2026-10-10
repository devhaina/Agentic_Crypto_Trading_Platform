using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Backtesting.Domain.Runs;

/// <summary>
/// One simulated round trip: an entry the replayed strategy signalled, and
/// the exit that closed it.
/// </summary>
/// <remarks>
/// Owned by exactly one <see cref="BacktestRun"/>; never looked up on its
/// own. <see cref="GrossPnl"/> excludes <see cref="Fees"/> and
/// <see cref="SlippageCost"/>; <see cref="NetPnl"/> is what actually reached
/// equity — the same gross/net split <c>Trade</c> (Portfolio Service, Phase
/// 6) makes for a real fill, kept here so a backtest result reads the same
/// way a live one does.
/// </remarks>
public sealed class BacktestTrade : Entity<BacktestTradeId>
{
    private BacktestTrade()
    {
    }

    private BacktestTrade(
        BacktestTradeId id,
        BacktestId backtestId,
        TradeAction side,
        DateTimeOffset entryTime,
        decimal entryPrice,
        decimal quantity,
        DateTimeOffset exitTime,
        decimal exitPrice,
        ExitReason exitReason,
        decimal grossPnl,
        decimal fees,
        decimal slippageCost,
        IReadOnlyList<string> reasonCodes)
        : base(id)
    {
        BacktestId = backtestId;
        Side = side;
        EntryTime = entryTime;
        EntryPrice = entryPrice;
        Quantity = quantity;
        ExitTime = exitTime;
        ExitPrice = exitPrice;
        ExitReason = exitReason;
        GrossPnl = grossPnl;
        Fees = fees;
        SlippageCost = slippageCost;
        ReasonCodesJoined = string.Join(',', reasonCodes);
    }

    public BacktestId BacktestId { get; private set; }

    /// <summary>Buy (long) or Sell (short). Never <see cref="TradeAction.Hold"/> — a hold opens nothing.</summary>
    public TradeAction Side { get; private set; }

    public DateTimeOffset EntryTime { get; private set; }

    public decimal EntryPrice { get; private set; }

    public decimal Quantity { get; private set; }

    public DateTimeOffset ExitTime { get; private set; }

    public decimal ExitPrice { get; private set; }

    public ExitReason ExitReason { get; private set; }

    /// <summary>P&amp;L before fees and slippage.</summary>
    public decimal GrossPnl { get; private set; }

    public decimal Fees { get; private set; }

    public decimal SlippageCost { get; private set; }

    /// <summary>What actually reached equity: <see cref="GrossPnl"/> minus <see cref="Fees"/> and <see cref="SlippageCost"/>.</summary>
    public decimal NetPnl => GrossPnl - Fees - SlippageCost;

    /// <summary>Comma-joined storage for <see cref="ReasonCodes"/> — see <c>Signal.ReasonCodesJoined</c> (Strategy Service) for the same convention.</summary>
    public string ReasonCodesJoined { get; private set; } = string.Empty;

    /// <summary>The strategy's own reason codes for the entry that opened this trade.</summary>
    public IReadOnlyList<string> ReasonCodes => ReasonCodesJoined.Length == 0
        ? []
        : ReasonCodesJoined.Split(',');

    /// <summary>
    /// Records one simulated round trip. In production, called only by
    /// <c>Simulation.BacktestSimulator</c>, which produces the full trade
    /// list a <see cref="BacktestRun"/> is then given in one call to
    /// <see cref="BacktestRun.Complete"/>.
    /// </summary>
    public static BacktestTrade Record(
        BacktestId backtestId,
        TradeAction side,
        DateTimeOffset entryTime,
        decimal entryPrice,
        decimal quantity,
        DateTimeOffset exitTime,
        decimal exitPrice,
        ExitReason exitReason,
        decimal grossPnl,
        decimal fees,
        decimal slippageCost,
        IReadOnlyList<string> reasonCodes)
        => new(
            BacktestTradeId.New(), backtestId, side, entryTime, entryPrice, quantity, exitTime, exitPrice,
            exitReason, grossPnl, fees, slippageCost, reasonCodes);
}
