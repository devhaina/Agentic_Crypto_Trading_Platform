using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agentiva.MarketData.Infrastructure.Binance;

/// <summary>JSON options for parsing Binance's own wire payloads.</summary>
/// <remarks>
/// Deliberately separate from <c>AgentivaJson</c>: that options object encodes
/// this platform's own wire conventions (camelCase, decimals as strings on
/// write) for payloads this platform designs. Binance's payloads use
/// abbreviated, mixed-case keys this platform does not control, and every
/// field here is mapped explicitly with <see cref="JsonPropertyNameAttribute"/>
/// rather than through a naming policy.
/// </remarks>
public static class BinanceJson
{
    /// <summary>Canonical options instance for deserialising exchange payloads. Treat as immutable.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create() => new()
    {
        // Binance quotes every financial field as a JSON string
        // ("c":"43250.12000000") to avoid the same double-precision loss this
        // platform's own AgentivaJson avoids by writing decimals as strings.
        NumberHandling = JsonNumberHandling.AllowReadingFromString,

        // A real Binance payload carries many fields this service does not
        // map (ignore price, weighted average price, 24h high/low, and so
        // on). Rejecting them the way AgentivaJson rejects an unknown field
        // on an internal contract would turn Binance adding a field to its
        // own API into an outage for this service.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };
}
