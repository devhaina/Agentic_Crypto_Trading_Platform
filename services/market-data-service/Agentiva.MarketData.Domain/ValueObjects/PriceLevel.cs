using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.MarketData.Domain.ValueObjects;

/// <summary>One resting price level in an order book, on either side.</summary>
/// <remarks>
/// <see cref="Quantity"/> is deliberately the raw <see cref="Agentiva.BuildingBlocks.Domain.Primitives.Quantity"/>
/// type rather than <c>Quantity.Create</c>, which rejects zero: Binance's partial
/// depth stream legitimately reports a level with quantity <c>0</c> to mean
/// "this level was removed", and a level that cannot represent that is unusable
/// for maintaining book state.
/// </remarks>
public sealed record PriceLevel(Price Price, decimal Quantity);
