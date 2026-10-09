using Agentiva.MarketData.Application.Contracts;

namespace Agentiva.MarketData.Application.Abstractions;

/// <summary>
/// Live connection and freshness state for every configured symbol, satisfied
/// by the Infrastructure component that owns the exchange connections.
/// </summary>
public interface IMarketFeedStatusProvider
{
    /// <summary>Status for every symbol this service is configured to ingest.</summary>
    IReadOnlyList<SymbolFeedStatusDto> GetStatus();
}
