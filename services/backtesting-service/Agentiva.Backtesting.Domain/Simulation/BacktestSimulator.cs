using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.BuildingBlocks.TradingRules.Indicators;
using Agentiva.BuildingBlocks.TradingRules.Strategies;
using Agentiva.Backtesting.Domain.Runs;

namespace Agentiva.Backtesting.Domain.Simulation;

/// <summary>
/// Replays one <see cref="IStrategy"/> bar by bar over historical candles and
/// produces the trades and equity curve a <see cref="BacktestRun"/> is scored
/// on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Look-ahead safety</b> is structural, not a rule someone has to
/// remember: the rolling window fed to the strategy on bar <c>i</c> never
/// contains bar <c>i + 1</c> or later, because it is built the same way
/// <c>CandleBuffer</c> builds it live — one <c>Append</c> per bar, in order.
/// A position's stop and take-profit, once set from bar <c>i</c>'s signal,
/// are checked starting bar <c>i + 1</c>, never against the bar that created
/// them.
/// </para>
/// <para>
/// <b>One position at a time.</b> While a position is open, every bar is
/// spent checking its stop and take-profit; a new signal is never evaluated
/// until it closes. This is a deliberate simplification over "reverse on an
/// opposite signal" or pyramiding, documented in
/// docs/architecture/known-limitations.md, not an oversight — it keeps the
/// position-sizing and equity bookkeeping unambiguous.
/// </para>
/// <para>
/// <b>Fees and slippage</b> are modelled exactly the way
/// <c>RiskPolicy.TakerFeePercent</c>/<c>SlippageAssumptionPercent</c> already
/// describe themselves: a percentage drag on notional, charged on both the
/// entry and the exit leg, deducted from equity immediately rather than
/// walking the fill price itself. <see cref="BacktestTrade.GrossPnl"/> is the
/// frictionless P&amp;L; fees and slippage are subtracted separately so a
/// result can show which one actually cost more.
/// </para>
/// <para>
/// <b>Position sizing</b> risks a fixed percentage of current equity per
/// trade against the strategy's own ATR-derived stop distance — the same
/// risk-based sizing a deterministic risk policy would apply live, scaled by
/// how wide that particular signal's stop happens to be.
/// </para>
/// </remarks>
public static class BacktestSimulator
{
    public static SimulationOutput Run(
        IStrategy strategy,
        IReadOnlyList<PriceBar> bars,
        BacktestId backtestId,
        string symbol,
        string timeframe,
        int candleBufferCapacity,
        decimal startingCapital,
        Percentage feePercent,
        Percentage slippagePercent,
        Percentage riskPerTradePercent)
    {
        var window = new List<PriceBar>(candleBufferCapacity);
        var trades = new List<BacktestTrade>();
        var equityCurve = new List<EquityPoint>(bars.Count);

        var equity = startingCapital;
        OpenPosition? open = null;

        foreach (var bar in bars)
        {
            Append(window, bar, candleBufferCapacity);

            if (open is { } position)
            {
                var exit = DetermineExit(position, bar);

                if (exit is { } triggered)
                {
                    var trade = Close(backtestId, position, bar.OpenTime, triggered.Price, triggered.Reason,
                        feePercent, slippagePercent, ref equity);
                    trades.Add(trade);
                    open = null;
                    equityCurve.Add(new EquityPoint(bar.OpenTime, equity));
                    continue;
                }

                equityCurve.Add(new EquityPoint(bar.OpenTime, equity + UnrealizedPnl(position, bar.Close)));
                continue;
            }

            if (equity <= 0m || window.Count < strategy.MinimumBars)
            {
                equityCurve.Add(new EquityPoint(bar.OpenTime, equity));
                continue;
            }

            var context = new StrategyEvaluationContext(symbol, timeframe, window.ToArray(), IndicatorSet.Compute(window));
            var decision = strategy.Evaluate(context);

            if (decision.Action is TradeAction.Hold || decision.StopLoss is null)
            {
                equityCurve.Add(new EquityPoint(bar.OpenTime, equity));
                continue;
            }

            open = Open(decision, bar.OpenTime, equity, riskPerTradePercent, feePercent, slippagePercent, ref equity);
            equityCurve.Add(new EquityPoint(bar.OpenTime, equity));
        }

        if (open is { } stillOpen && bars.Count > 0)
        {
            var lastBar = bars[^1];
            var trade = Close(backtestId, stillOpen, lastBar.OpenTime, lastBar.Close, ExitReason.EndOfData,
                feePercent, slippagePercent, ref equity);
            trades.Add(trade);
            equityCurve.Add(new EquityPoint(lastBar.OpenTime, equity));
        }

        return new SimulationOutput(trades, equityCurve);
    }

    private static void Append(List<PriceBar> window, PriceBar bar, int capacity)
    {
        window.Add(bar);

        if (window.Count > capacity)
        {
            window.RemoveAt(0);
        }
    }

    private static OpenPosition Open(
        StrategyEvaluationResult decision,
        DateTimeOffset entryTime,
        decimal equityBeforeEntry,
        Percentage riskPerTradePercent,
        Percentage feePercent,
        Percentage slippagePercent,
        ref decimal equity)
    {
        var stopDistance = Math.Abs(decision.EntryPrice - decision.StopLoss!.Value);
        var riskAmount = riskPerTradePercent.Of(equityBeforeEntry);

        // No leverage: a trade can never commit more notional than the
        // account currently holds, however wide the risk-sized quantity
        // would otherwise be — a blown-up account cannot owe more than it has.
        var riskSizedQuantity = stopDistance > 0m ? riskAmount / stopDistance : 0m;
        var maxAffordableQuantity = decision.EntryPrice > 0m ? equityBeforeEntry / decision.EntryPrice : 0m;
        var quantity = Math.Min(riskSizedQuantity, maxAffordableQuantity);

        var entryFee = feePercent.Of(decision.EntryPrice * quantity);
        var entrySlippage = slippagePercent.Of(decision.EntryPrice * quantity);
        equity -= entryFee + entrySlippage;

        return new OpenPosition(
            decision.Action, entryTime, decision.EntryPrice, quantity, decision.StopLoss.Value,
            decision.TakeProfit, decision.ReasonCodes, entryFee, entrySlippage);
    }

    private static BacktestTrade Close(
        BacktestId backtestId,
        OpenPosition position,
        DateTimeOffset exitTime,
        decimal exitPrice,
        ExitReason reason,
        Percentage feePercent,
        Percentage slippagePercent,
        ref decimal equity)
    {
        var grossPnl = position.Side == TradeAction.Buy
            ? (exitPrice - position.EntryPrice) * position.Quantity
            : (position.EntryPrice - exitPrice) * position.Quantity;

        var exitFee = feePercent.Of(exitPrice * position.Quantity);
        var exitSlippage = slippagePercent.Of(exitPrice * position.Quantity);

        var fees = position.EntryFee + exitFee;
        var slippageCost = position.EntrySlippage + exitSlippage;

        equity += grossPnl - exitFee - exitSlippage;

        return BacktestTrade.Record(
            backtestId, position.Side, position.EntryTime, position.EntryPrice, position.Quantity,
            exitTime, exitPrice, reason, grossPnl, fees, slippageCost, position.ReasonCodes);
    }

    private static decimal UnrealizedPnl(OpenPosition position, decimal currentPrice)
        => position.Side == TradeAction.Buy
            ? (currentPrice - position.EntryPrice) * position.Quantity
            : (position.EntryPrice - currentPrice) * position.Quantity;

    /// <summary>
    /// Checks one bar's high/low against an open position's stop and target.
    /// </summary>
    /// <remarks>
    /// When a bar's range touches both levels, the stop is assumed to have
    /// been hit first — the conservative convention every simple backtester
    /// without intrabar tick data uses, because assuming the better-case
    /// order would systematically overstate performance. A gap through a
    /// level (the bar opened beyond it) fills at the worse of the bar's open
    /// or the level itself, not at the level a real fill could not have
    /// reached.
    /// </remarks>
    private static (decimal Price, ExitReason Reason)? DetermineExit(OpenPosition position, PriceBar bar)
    {
        if (position.Side == TradeAction.Buy)
        {
            if (bar.Low <= position.StopLoss)
            {
                return (Math.Min(bar.Open, position.StopLoss), ExitReason.StopLoss);
            }

            if (position.TakeProfit is { } take && bar.High >= take)
            {
                return (Math.Max(bar.Open, take), ExitReason.TakeProfit);
            }

            return null;
        }

        if (bar.High >= position.StopLoss)
        {
            return (Math.Max(bar.Open, position.StopLoss), ExitReason.StopLoss);
        }

        if (position.TakeProfit is { } shortTake && bar.Low <= shortTake)
        {
            return (Math.Min(bar.Open, shortTake), ExitReason.TakeProfit);
        }

        return null;
    }

    private sealed record OpenPosition(
        TradeAction Side,
        DateTimeOffset EntryTime,
        decimal EntryPrice,
        decimal Quantity,
        decimal StopLoss,
        decimal? TakeProfit,
        IReadOnlyList<string> ReasonCodes,
        decimal EntryFee,
        decimal EntrySlippage);
}
