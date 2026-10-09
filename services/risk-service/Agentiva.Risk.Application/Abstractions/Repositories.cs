using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Risk.Domain.Entities;
using Agentiva.Risk.Domain.Policies;

namespace Agentiva.Risk.Application.Abstractions;

/// <summary>Access to the risk policies this service owns.</summary>
public interface IRiskPolicyRepository
{
    /// <summary>Returns the active default policy.</summary>
    /// <remarks>
    /// Never returns null in a correctly seeded database. A missing default is a
    /// fail-closed condition: the service refuses to evaluate rather than
    /// falling back to permissive limits, because "no policy" must never be
    /// interpreted as "no limits".
    /// </remarks>
    Task<RiskPolicy?> GetDefaultAsync(CancellationToken cancellationToken);

    Task<RiskPolicy?> GetByIdAsync(RiskPolicyId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<RiskPolicy>> ListAsync(CancellationToken cancellationToken);

    void Add(RiskPolicy policy);
}

/// <summary>Append-only store of risk evaluation records.</summary>
public interface IRiskCheckRepository
{
    void Add(RiskCheckRecord record);

    Task<RiskCheckRecord?> GetByIdAsync(RiskCheckId id, CancellationToken cancellationToken);

    /// <summary>Most recent evaluations, newest first.</summary>
    Task<IReadOnlyList<RiskCheckRecord>> ListRecentAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Reports whether the global kill switch is engaged and whether trading is enabled.
/// </summary>
/// <remarks>
/// Separated behind an interface because the authoritative source changes by
/// phase: configuration in Phase 1, a Redis-backed flag maintained by the
/// reconciliation and risk services later. The evaluator only needs the answer.
/// </remarks>
public interface IPlatformStateProvider
{
    /// <summary>Whether the global kill switch is engaged.</summary>
    Task<bool> IsKillSwitchEngagedAsync(CancellationToken cancellationToken);

    /// <summary>Whether new order admission is enabled.</summary>
    Task<bool> IsTradingEnabledAsync(CancellationToken cancellationToken);

    /// <summary>The effective trading mode.</summary>
    TradingMode EffectiveTradingMode { get; }

    /// <summary>
    /// Whether configuration forces the kill switch engaged regardless of the
    /// Redis flag. An operator calling <see cref="ReleaseKillSwitchAsync"/>
    /// while this is true clears the flag but the switch stays functionally
    /// engaged, which the caller needs to know rather than being told the
    /// release succeeded.
    /// </summary>
    bool IsKillSwitchForcedByConfiguration { get; }

    /// <summary>Engages the kill switch and publishes the activation event.</summary>
    Task EngageKillSwitchAsync(
        string trigger, string detail, string activatedBy, CancellationToken cancellationToken);

    /// <summary>Releases the kill switch and publishes the deactivation event.</summary>
    Task ReleaseKillSwitchAsync(
        string deactivatedBy, string justification, CancellationToken cancellationToken);
}

/// <summary>Supplies the exchange precision filters for a symbol.</summary>
/// <remarks>
/// Phase 1 serves a configured table of filters for the MVP symbols. From Phase
/// 2 these are loaded from the exchange at startup and refreshed periodically,
/// because exchanges change filters without notice and a stale step size means
/// every order is rejected.
/// </remarks>
public interface IInstrumentPrecisionProvider
{
    Task<InstrumentPrecision?> GetAsync(Symbol symbol, CancellationToken cancellationToken);
}
