namespace Agentiva.Contracts.Events.Portfolio;

/// <summary>A position's size, average entry or P&amp;L changed.</summary>
public sealed record PositionUpdated : IntegrationEvent
{
    public override string EventType => EventTypes.Portfolio.PositionUpdated;

    public required Guid PositionId { get; init; }

    public required Guid TradingAccountId { get; init; }

    public required string Symbol { get; init; }

    /// <summary><c>LONG</c>, <c>SHORT</c> or <c>FLAT</c>.</summary>
    public required string Direction { get; init; }

    public required decimal Quantity { get; init; }

    /// <summary>Weighted average entry price across every contributing fill.</summary>
    public required decimal AverageEntryPrice { get; init; }

    /// <summary>Realised P&amp;L in quote asset, net of fees.</summary>
    public required decimal RealizedPnl { get; init; }

    /// <summary>Unrealised P&amp;L at the current mark price, in quote asset.</summary>
    public required decimal UnrealizedPnl { get; init; }

    /// <summary>Mark price used for the unrealised figure, so the number is reproducible.</summary>
    public required decimal MarkPrice { get; init; }

    public required string QuoteAsset { get; init; }
}

/// <summary>Portfolio-level valuation changed.</summary>
public sealed record PortfolioUpdated : IntegrationEvent
{
    public override string EventType => EventTypes.Portfolio.PortfolioUpdated;

    public required Guid PortfolioId { get; init; }

    public required Guid TradingAccountId { get; init; }

    /// <summary>Total account value: cash plus marked positions, in quote asset.</summary>
    public required decimal TotalValue { get; init; }

    /// <summary>Unencumbered cash available to open new positions.</summary>
    public required decimal AvailableBalance { get; init; }

    /// <summary>Notional value of all open positions, in quote asset.</summary>
    public required decimal TotalExposure { get; init; }

    /// <summary>Exposure as a percentage of total value.</summary>
    public required decimal ExposurePercent { get; init; }

    public required int OpenPositionCount { get; init; }

    public required string QuoteAsset { get; init; }
}

/// <summary>Profit and loss figures changed.</summary>
public sealed record PnlUpdated : IntegrationEvent
{
    public override string EventType => EventTypes.Portfolio.PnlUpdated;

    public required Guid TradingAccountId { get; init; }

    /// <summary>Realised P&amp;L since the start of the current UTC trading day.</summary>
    public required decimal DailyRealizedPnl { get; init; }

    public required decimal DailyUnrealizedPnl { get; init; }

    /// <summary>Lifetime realised P&amp;L, net of all fees.</summary>
    public required decimal TotalRealizedPnl { get; init; }

    /// <summary>Peak-to-trough decline from the high-water mark, as a percentage.</summary>
    public required decimal CurrentDrawdownPercent { get; init; }

    /// <summary>UTC date the daily figures apply to.</summary>
    public required DateOnly TradingDay { get; init; }

    public required string QuoteAsset { get; init; }
}

/// <summary>An asset balance changed.</summary>
public sealed record BalanceUpdated : IntegrationEvent
{
    public override string EventType => EventTypes.Portfolio.BalanceUpdated;

    public required Guid TradingAccountId { get; init; }

    public required string Asset { get; init; }

    /// <summary>Balance available for new orders.</summary>
    public required decimal Free { get; init; }

    /// <summary>Balance reserved against resting orders.</summary>
    public required decimal Locked { get; init; }
}

/// <summary>A round trip completed and its realised P&amp;L is final.</summary>
public sealed record TradeCompleted : IntegrationEvent
{
    public override string EventType => EventTypes.Portfolio.TradeCompleted;

    public required Guid TradeId { get; init; }

    public required Guid TradingAccountId { get; init; }

    public required string Symbol { get; init; }

    public required string Side { get; init; }

    public required decimal EntryPrice { get; init; }

    public required decimal ExitPrice { get; init; }

    public required decimal Quantity { get; init; }

    /// <summary>Realised P&amp;L net of all fees on both legs.</summary>
    public required decimal RealizedPnl { get; init; }

    /// <summary>Total fees across both legs. Reported separately so gross and net are both visible.</summary>
    public required decimal TotalFees { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }

    public required DateTimeOffset ClosedAt { get; init; }

    /// <summary>Originating signal, when the trade came from one.</summary>
    public Guid? SignalId { get; init; }

    public Guid? StrategyId { get; init; }

    public required string QuoteAsset { get; init; }
}
