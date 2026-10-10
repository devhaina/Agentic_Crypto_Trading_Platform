namespace Agentiva.BuildingBlocks.TradingRules.Indicators;

/// <summary>
/// One closed OHLCV bar, as the Strategy Service itself understands it.
/// </summary>
/// <remarks>
/// Deliberately not the Market Data Service's <c>Candle</c> entity. Services
/// share only published event contracts, never each other's domain types — a
/// change to how the Market Data Service models a candle must never force a
/// redeploy of the Strategy Service. This is built directly from the plain
/// primitives on the consumed <c>market.candle.created</c> event.
/// </remarks>
public sealed record PriceBar(
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);
