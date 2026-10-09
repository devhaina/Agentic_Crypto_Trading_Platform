using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agentiva.Execution.Infrastructure.Binance;

/// <summary>JSON options for parsing Binance's own wire payloads.</summary>
/// <remarks>
/// Deliberately separate from <c>AgentivaJson</c>, mirroring
/// <c>MarketData.Infrastructure.Binance.BinanceJson</c>: that options object
/// encodes this platform's own wire conventions for payloads this platform
/// designs, while Binance's payloads use abbreviated, mixed-case keys this
/// platform does not control.
/// </remarks>
public static class BinanceJson
{
    /// <summary>Canonical options instance for deserialising exchange payloads. Treat as immutable.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create() => new()
    {
        // Binance quotes every financial field as a JSON string
        // ("price":"43250.12000000") to avoid double-precision loss.
        NumberHandling = JsonNumberHandling.AllowReadingFromString,

        // A real order response carries fields this adapter does not map
        // (self-trade prevention mode, working time, and so on). Rejecting
        // them would turn Binance adding a field to its own API into an
        // outage for this service.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };
}
