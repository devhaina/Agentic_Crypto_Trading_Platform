using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Strategy.Domain.Entities;

/// <summary>
/// The recorded outcome of one strategy evaluation — a decision to buy, sell,
/// or explicitly stand aside.
/// </summary>
/// <remarks>
/// Every evaluation is recorded, including <see cref="TradeAction.Hold"/>: the
/// platform's own <c>TradeAction</c> enum documents a hold as "modelled so
/// that a decision to stand aside is auditable", and an engine that only kept
/// rows for the decisions it acted on could not be audited against the
/// decisions it declined to act on. Only an actionable decision — buy or
/// sell — raises <see cref="SignalCreatedDomainEvent"/>: a hold is not a
/// signal to anything downstream, it is the absence of one.
/// </remarks>
public sealed class Signal : AggregateRoot<SignalId>
{
    private Signal()
    {
    }

    private Signal(
        SignalId id,
        StrategyId strategyId,
        string strategyName,
        string strategyVersion,
        Symbol symbol,
        string timeframe,
        TradeAction action,
        Percentage confidence,
        Price entryPrice,
        decimal? stopLoss,
        decimal? takeProfit,
        IReadOnlyList<string> reasonCodes,
        DateTimeOffset computedAt)
        : base(id)
    {
        StrategyId = strategyId;
        StrategyName = strategyName;
        StrategyVersion = strategyVersion;
        Symbol = symbol;
        Timeframe = timeframe;
        Action = action;
        Confidence = confidence;
        EntryPrice = entryPrice;
        StopLoss = stopLoss;
        TakeProfit = takeProfit;
        ReasonCodesJoined = string.Join(',', reasonCodes);
        ComputedAt = computedAt;
    }

    public StrategyId StrategyId { get; private set; }

    public string StrategyName { get; private set; } = string.Empty;

    public string StrategyVersion { get; private set; } = string.Empty;

    public Symbol Symbol { get; private set; }

    public string Timeframe { get; private set; } = string.Empty;

    public TradeAction Action { get; private set; }

    public Percentage Confidence { get; private set; }

    public Price EntryPrice { get; private set; }

    public decimal? StopLoss { get; private set; }

    public decimal? TakeProfit { get; private set; }

    /// <summary>
    /// Reason codes joined with a comma for storage. Every code is itself
    /// drawn from a fixed, comma-free vocabulary (see each strategy's own
    /// reason codes), so this is a safe, simple encoding rather than
    /// warranting a child table or a jsonb column for what is always a short,
    /// flat list.
    /// </summary>
    public string ReasonCodesJoined { get; private set; } = string.Empty;

    public IReadOnlyList<string> ReasonCodes => ReasonCodesJoined.Length == 0
        ? []
        : ReasonCodesJoined.Split(',');

    public DateTimeOffset ComputedAt { get; private set; }

    /// <exception cref="DomainException">
    /// A buy or sell decision is missing a stop-loss or take-profit, or confidence is out of range.
    /// </exception>
    public static Signal Create(
        StrategyId strategyId,
        string strategyName,
        string strategyVersion,
        string symbol,
        string timeframe,
        TradeAction action,
        decimal confidence,
        decimal entryPrice,
        decimal? stopLoss,
        decimal? takeProfit,
        IReadOnlyList<string> reasonCodes,
        DateTimeOffset computedAt)
    {
        if (action != TradeAction.Hold && (stopLoss is null || takeProfit is null))
        {
            throw new DomainException(
                "domain.signal.missing_protective_levels",
                $"A {action} signal for {symbol} must carry both a stop-loss and a take-profit.");
        }

        var signal = new Signal(
            SignalId.New(),
            strategyId,
            strategyName,
            strategyVersion,
            Symbol.Create(symbol),
            timeframe,
            action,
            Percentage.FromFraction(confidence),
            Price.Create(entryPrice),
            stopLoss,
            takeProfit,
            reasonCodes,
            computedAt);

        if (action != TradeAction.Hold)
        {
            signal.Raise(new SignalCreatedDomainEvent(
                computedAt,
                signal.Id.Value,
                signal.Symbol.Value,
                action.ToString().ToUpperInvariant(),
                confidence,
                entryPrice,
                stopLoss,
                takeProfit,
                strategyId.Value,
                strategyName,
                strategyVersion,
                timeframe,
                reasonCodes,
                computedAt));
        }

        return signal;
    }
}
