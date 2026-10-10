using System.ComponentModel.DataAnnotations;

namespace Agentiva.Portfolio.Application.Configuration;

/// <summary>
/// Starting state for an account this service has never seen a fill for.
/// </summary>
/// <remarks>
/// There is no deposit or funding flow anywhere in the platform, so a
/// trading account's cash balance has to start somewhere — this is that
/// somewhere, the same role <c>PaperPortfolioOptions.StartingEquity</c>
/// played in the Trading Service before this service existed. Lives in the
/// Application layer, not Infrastructure, because the query handlers that
/// need it must not depend on this service's own Infrastructure project —
/// Infrastructure binds it from configuration at startup.
/// </remarks>
public sealed class PortfolioOptions
{
    public const string SectionName = "Portfolio";

    /// <summary>Cash balance an account starts with before its first fill.</summary>
    [Range(typeof(decimal), "0", "100000000")]
    public decimal StartingCashBalance { get; set; } = 10_000m;

    /// <summary>Quote asset new accounts and positions are denominated in.</summary>
    [Required]
    public string QuoteAsset { get; set; } = "USDT";
}
