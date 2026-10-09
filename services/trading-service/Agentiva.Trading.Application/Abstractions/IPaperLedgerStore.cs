namespace Agentiva.Trading.Application.Abstractions;

/// <summary>Operator-set paper-trading ledger state.</summary>
/// <param name="Equity">Total simulated account value.</param>
/// <param name="AvailableBalance">Unencumbered simulated balance.</param>
/// <param name="CurrentExposure">Simulated notional of all open positions, portfolio-wide.</param>
/// <param name="OpenPositionCount">Simulated open position count.</param>
/// <param name="DailyPnl">Simulated P&amp;L for the current UTC day. Negative is a loss.</param>
/// <param name="UpdatedAt">When an operator last set this state.</param>
/// <param name="UpdatedBy">Operator user id who last set this state.</param>
/// <remarks>
/// Deliberately portfolio-wide only, with no per-symbol breakdown. A real
/// per-symbol exposure figure needs real position tracking, which needs real
/// fills — the Execution Service (Phase 5) and the Portfolio Service
/// (Phase 6) neither of which exist yet. This ledger is an operator-adjusted
/// paper baseline, not a position ledger: see
/// docs/architecture/known-limitations.md.
/// </remarks>
public sealed record PaperLedgerDto(
    decimal Equity,
    decimal AvailableBalance,
    decimal CurrentExposure,
    int OpenPositionCount,
    decimal DailyPnl,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

/// <summary>
/// Persists the operator-adjustable paper-trading ledger, satisfied by
/// Infrastructure.
/// </summary>
/// <remarks>
/// Replaces the Phase 1 hardcoded baseline in <c>PaperPortfolioOptions</c>
/// with a value an operator can change at runtime through an authenticated
/// endpoint, the same Redis-backed, fail-safe-default pattern as the Risk
/// Service's kill switch. It does not become a real portfolio: nothing here
/// tracks fills or derives exposure from actual trades, and it is never the
/// Portfolio Service's eventual source of truth.
/// </remarks>
public interface IPaperLedgerStore
{
    /// <summary>The current operator-set state, or <c>null</c> if none has been set.</summary>
    Task<PaperLedgerDto?> GetAsync(CancellationToken cancellationToken);

    Task SetAsync(
        decimal equity,
        decimal availableBalance,
        decimal currentExposure,
        int openPositionCount,
        decimal dailyPnl,
        string updatedBy,
        CancellationToken cancellationToken);

    /// <summary>Clears the operator-set state, reverting to the configured baseline.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Supplies the configured fallback ledger used when no operator value has
/// been set, satisfied by Infrastructure (which owns the configuration
/// options this reads).
/// </summary>
public interface IPaperLedgerDefaults
{
    /// <summary>The configured starting-equity baseline, with zero exposure and zero daily P&amp;L.</summary>
    PaperLedgerDto ConfiguredBaseline();
}
