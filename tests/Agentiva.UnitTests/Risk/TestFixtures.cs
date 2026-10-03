using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Domain.Evaluation;
using Agentiva.Risk.Domain.Policies;

namespace Agentiva.UnitTests.Risk;

/// <summary>
/// Shared fixtures for the risk tests.
/// </summary>
/// <remarks>
/// Values mirror real Binance BTCUSDT filters so that the tests exercise the
/// rounding and minimum-notional behaviour the platform actually meets in
/// production, rather than convenient round numbers that hide it.
/// </remarks>
internal static class TestFixtures
{
    public static readonly AssetCode Usdt = AssetCode.Create("USDT");
    public static readonly Symbol BtcUsdt = Symbol.Create("BTCUSDT");

    /// <summary>BTCUSDT spot filters: 0.01 tick, 0.00001 step, 5 USDT minimum notional.</summary>
    public static InstrumentPrecision BtcUsdtPrecision => new()
    {
        TickSize = 0.01m,
        StepSize = 0.00001m,
        MinQuantity = 0.00001m,
        MaxQuantity = 9000m,
        MinNotional = 5m,
        BaseAsset = AssetCode.Create("BTC"),
        QuoteAsset = Usdt
    };

    /// <summary>A symbol with a coarse step, used to exercise rounding to zero.</summary>
    public static InstrumentPrecision CoarsePrecision => new()
    {
        TickSize = 1m,
        StepSize = 1m,
        MinQuantity = 1m,
        MaxQuantity = 1000m,
        MinNotional = 100m,
        BaseAsset = AssetCode.Create("COARSE"),
        QuoteAsset = Usdt
    };

    public static RiskPolicy DefaultPolicy()
        => RiskPolicy.CreateDefault(DateTimeOffset.UnixEpoch, Usdt);

    /// <summary>
    /// A policy whose caps are set wide enough not to bind, so a test can
    /// isolate one specific constraint.
    /// </summary>
    /// <remarks>
    /// Needed because the conservative defaults bind early and often — the
    /// 1,000 USDT per-position cap and the 25% concentration limit both engage
    /// well before the risk budget does at realistic BTC prices. That is the
    /// correct production behaviour, but it hides whichever behaviour a given
    /// test is trying to observe.
    /// </remarks>
    public static RiskPolicy UncappedPolicy(
        decimal takerFeePercent = 0.1m,
        decimal slippagePercent = 0m,
        decimal maxRiskPerTradePercent = 0.5m)
        => RiskPolicy.Create(
            name: "test-uncapped",
            maxRiskPerTrade: Percentage.FromPercent(maxRiskPerTradePercent),
            maxPositionNotional: Money.Create(100_000_000m, Usdt),
            maxDailyLoss: Percentage.FromPercent(2m),
            maxPortfolioExposure: Percentage.FromPercent(100m),
            maxAssetConcentration: Percentage.FromPercent(100m),
            maxOpenPositions: 100,
            minConfidence: Percentage.FromPercent(60m),
            maxVolatility: Percentage.FromPercent(100m),
            requireStopLoss: true,
            requireTakeProfit: true,
            slippageAssumption: Percentage.FromPercent(slippagePercent),
            takerFee: Percentage.FromPercent(takerFeePercent),
            marketDataStalenessThreshold: TimeSpan.FromSeconds(30),
            now: DateTimeOffset.UnixEpoch,
            updatedBy: "test");

    /// <summary>
    /// A request that passes every check, so each test can vary one field and
    /// attribute the resulting rejection to exactly that field.
    /// </summary>
    public static RiskEvaluationRequest ValidRequest(RiskPolicy? policy = null)
    {
        policy ??= DefaultPolicy();

        return new RiskEvaluationRequest(
            TradingIntentId: Guid.CreateVersion7(),
            Symbol: BtcUsdt,
            Side: OrderSide.Buy,
            EntryPrice: Price.Create(100_000m),
            StopLoss: Price.Create(98_000m),
            TakeProfit: Price.Create(104_000m),
            Confidence: Percentage.FromPercent(75m),
            Policy: policy,
            Precision: BtcUsdtPrecision,
            PortfolioEquity: Money.Create(100_000m, Usdt),
            AvailableBalance: Money.Create(50_000m, Usdt),
            CurrentExposure: Money.Create(0m, Usdt),
            CurrentSymbolExposure: Money.Create(0m, Usdt),
            OpenPositionCount: 0,
            DailyPnl: Money.Create(0m, Usdt),
            SymbolVolatility: Percentage.FromPercent(40m),
            MarketDataAge: TimeSpan.FromSeconds(2),
            IsExchangeAvailable: true,
            IsKillSwitchEngaged: false,
            IsTradingEnabled: true,
            TradingMode: TradingMode.Paper,
            HasDuplicateOpenOrder: false,
            EvaluatedAt: DateTimeOffset.UnixEpoch);
    }
}
