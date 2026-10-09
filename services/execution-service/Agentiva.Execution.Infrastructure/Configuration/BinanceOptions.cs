using System.ComponentModel.DataAnnotations;

namespace Agentiva.Execution.Infrastructure.Configuration;

/// <summary>
/// Connection settings for Binance's trading REST API.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> and <see cref="ApiSecret"/> are deliberately not
/// <c>[Required]</c>: they stay blank whenever the effective trading mode is
/// Backtest or Paper, which never reaches <c>BinanceExecutionAdapter</c>. An
/// architecture test — <c>Only_the_execution_service_references_exchange_credentials</c>
/// — asserts that no other service's source references them.
/// </remarks>
public sealed class BinanceOptions
{
    public const string SectionName = "Binance";

    /// <summary>Whether to trade against the Binance Spot Testnet rather than production.</summary>
    public bool UseTestnet { get; set; } = true;

    /// <summary>Base REST endpoint, e.g. <c>https://testnet.binance.vision</c>.</summary>
    [Required]
    public string RestBaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Receive window Binance accepts a signed request within, in milliseconds.
    /// </summary>
    [Range(1000, 60000)]
    public int RecvWindowMs { get; set; } = 5000;
}

