using System.ComponentModel.DataAnnotations;

namespace Agentiva.MarketData.Infrastructure.Configuration;

/// <summary>Connection settings for Binance's public WebSocket market streams.</summary>
/// <remarks>
/// No API key or secret appears here, deliberately: every stream this service
/// subscribes to is public market data, and the platform's exchange
/// credentials belong to the Execution Service alone — see
/// docs/architecture/known-limitations.md on the AI trust boundary for why
/// that separation is load-bearing rather than incidental.
/// </remarks>
public sealed class BinanceOptions
{
    public const string SectionName = "Binance";

    /// <summary>Whether to connect to the Binance Spot Testnet rather than production.</summary>
    public bool UseTestnet { get; set; } = true;

    /// <summary>
    /// Base WebSocket endpoint, e.g. <c>wss://testnet.binance.vision/ws</c>. A single
    /// stream's full URL is this value plus <c>/&lt;streamName&gt;</c>.
    /// </summary>
    [Required]
    public string WsBaseUrl { get; set; } = string.Empty;

    /// <summary>Price levels per side requested from the partial depth stream.</summary>
    [Range(5, 20)]
    public int OrderBookDepth { get; set; } = 20;

    /// <summary>Update frequency of the partial depth stream, in milliseconds.</summary>
    [Range(100, 5000)]
    public int OrderBookUpdateIntervalMs { get; set; } = 1000;

    /// <summary>Delay before the first reconnect attempt after a dropped stream.</summary>
    [Range(1, 60)]
    public int InitialReconnectDelaySeconds { get; set; } = 1;

    /// <summary>Ceiling the exponential reconnect backoff is capped at.</summary>
    [Range(1, 300)]
    public int MaxReconnectDelaySeconds { get; set; } = 30;

    /// <summary>Exchange identifier recorded on every normalised record and published event.</summary>
    public string ExchangeName { get; set; } = "binance";

    /// <summary>Builds the full WebSocket URL for one raw stream.</summary>
    public Uri BuildStreamUri(string streamName) => new($"{WsBaseUrl.TrimEnd('/')}/{streamName}");
}
