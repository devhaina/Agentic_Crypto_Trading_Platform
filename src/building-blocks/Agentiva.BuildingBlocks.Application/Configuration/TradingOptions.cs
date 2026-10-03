using System.ComponentModel.DataAnnotations;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.BuildingBlocks.Application.Configuration;

/// <summary>
/// Platform-wide trading mode and kill switch configuration.
/// </summary>
/// <remarks>
/// Lives in the application building block rather than in ServiceDefaults so
/// that a service's Application layer can read it without taking a dependency
/// on ASP.NET Core. Enforced at the execution boundary. Configuration alone is not the control — the Execution Service
/// re-checks the mode immediately before contacting an exchange — but having one
/// shared shape means the dashboard, the risk gate and the executor cannot
/// disagree about which mode the platform is in.
/// </remarks>
public sealed class TradingOptions
{
    public const string SectionName = "Trading";

    /// <summary>
    /// Active execution mode. Defaults to <see cref="TradingMode.Paper"/>.
    /// </summary>
    /// <remarks>
    /// The default is paper trading, chosen so that a missing or malformed
    /// configuration value cannot result in real orders. A fail-safe default is
    /// the only acceptable one for a setting of this kind.
    /// </remarks>
    public TradingMode Mode { get; set; } = TradingMode.Paper;

    /// <summary>
    /// Second, independent switch that must also be true before
    /// <see cref="TradingMode.Live"/> is honoured.
    /// </summary>
    /// <remarks>
    /// Two separate settings are required for live trading on purpose. A single
    /// flag is one typo, one bad merge or one copied environment file away from
    /// trading real funds; requiring an explicit second acknowledgement in a
    /// different variable makes that mistake much harder to make by accident.
    /// </remarks>
    public bool AllowLive { get; set; }

    /// <summary>Whether the global kill switch is engaged, blocking all new orders.</summary>
    public bool KillSwitchEnabled { get; set; }

    /// <summary>
    /// The mode that will actually be used, after applying the live-trading guard.
    /// </summary>
    /// <remarks>
    /// Requesting <see cref="TradingMode.Live"/> without
    /// <see cref="AllowLive"/> degrades to paper rather than throwing: refusing
    /// to start would be a worse outcome than running safely in paper mode, and
    /// the discrepancy is logged loudly at startup.
    /// </remarks>
    public TradingMode EffectiveMode => Mode == TradingMode.Live && !AllowLive
        ? TradingMode.Paper
        : Mode;

    /// <summary>Whether configuration is asking for live trading but the guard forbids it.</summary>
    public bool IsLiveRequestedButBlocked => Mode == TradingMode.Live && !AllowLive;

    /// <summary>Quote asset used for portfolio valuation and risk limits.</summary>
    [Required]
    public string QuoteAsset { get; set; } = "USDT";
}
