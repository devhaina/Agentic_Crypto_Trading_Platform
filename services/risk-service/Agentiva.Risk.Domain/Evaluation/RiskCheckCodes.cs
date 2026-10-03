namespace Agentiva.Risk.Domain.Evaluation;

/// <summary>
/// Stable reason codes emitted by the risk gate.
/// </summary>
/// <remarks>
/// Part of the platform's audit contract. These strings appear in audit records,
/// Grafana panels, alert rules and the dashboard's rejection list, so an
/// existing code is never reworded — a new one is added instead.
/// </remarks>
public static class RiskCheckCodes
{
    public const string KillSwitch = "risk.rejected.kill_switch_engaged";
    public const string TradingDisabled = "risk.rejected.trading_disabled";
    public const string TradingMode = "risk.rejected.trading_mode_not_permitted";
    public const string ExchangeUnavailable = "risk.rejected.exchange_unavailable";
    public const string MarketDataStale = "risk.rejected.market_data_stale";
    public const string RiskServiceDegraded = "risk.rejected.risk_inputs_unavailable";

    public const string MinConfidence = "risk.rejected.confidence_below_minimum";
    public const string StopLossRequired = "risk.rejected.stop_loss_required";
    public const string TakeProfitRequired = "risk.rejected.take_profit_required";
    public const string StopLossDirection = "risk.rejected.stop_loss_wrong_side";
    public const string TakeProfitDirection = "risk.rejected.take_profit_wrong_side";

    public const string MaxVolatility = "risk.rejected.volatility_above_maximum";
    public const string MaxOpenPositions = "risk.rejected.max_open_positions";
    public const string MaxDailyLoss = "risk.rejected.max_daily_loss";
    public const string MaxPortfolioExposure = "risk.rejected.max_portfolio_exposure";
    public const string MaxAssetConcentration = "risk.rejected.max_asset_concentration";
    public const string MaxPositionSize = "risk.rejected.max_position_size";
    public const string InsufficientBalance = "risk.rejected.insufficient_balance";
    public const string DuplicateOrder = "risk.rejected.duplicate_order";
    public const string BelowExchangeMinimum = "risk.rejected.below_exchange_minimum";
    public const string RiskBudgetExhausted = "risk.rejected.risk_budget_exhausted";
    public const string UnknownSymbol = "risk.rejected.unknown_symbol";

    public const string Approved = "risk.approved";
}

/// <summary>Names of the individual checks, used as labels and in audit records.</summary>
public static class RiskCheckNames
{
    public const string KillSwitch = "KillSwitch";
    public const string TradingMode = "TradingMode";
    public const string ExchangeAvailability = "ExchangeAvailability";
    public const string MarketDataFreshness = "MarketDataFreshness";
    public const string MinimumConfidence = "MinimumConfidence";
    public const string StopLossRequirement = "StopLossRequirement";
    public const string TakeProfitRequirement = "TakeProfitRequirement";
    public const string ProtectiveLevelDirection = "ProtectiveLevelDirection";
    public const string MaxVolatility = "MaxVolatility";
    public const string MaxOpenPositions = "MaxOpenPositions";
    public const string MaxDailyLoss = "MaxDailyLoss";
    public const string MaxPortfolioExposure = "MaxPortfolioExposure";
    public const string MaxAssetConcentration = "MaxAssetConcentration";
    public const string MaxPositionSize = "MaxPositionSize";
    public const string AvailableBalance = "AvailableBalance";
    public const string DuplicateOrder = "DuplicateOrder";
    public const string PositionSizing = "PositionSizing";
}
