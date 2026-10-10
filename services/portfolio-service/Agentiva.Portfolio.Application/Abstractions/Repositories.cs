using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Portfolio.Domain.Accounts;
using Agentiva.Portfolio.Domain.Positions;
using Agentiva.Portfolio.Domain.Trades;

namespace Agentiva.Portfolio.Application.Abstractions;

/// <summary>Access to the positions this service owns.</summary>
public interface IPositionRepository
{
    void Add(Position position);

    Task<Position?> GetAsync(TradingAccountId tradingAccountId, Symbol symbol, CancellationToken cancellationToken);

    /// <summary>Every position with non-zero quantity for an account, in no particular order.</summary>
    Task<IReadOnlyList<Position>> ListOpenAsync(TradingAccountId tradingAccountId, CancellationToken cancellationToken);

    /// <summary>
    /// Lifetime realised P&amp;L summed across every position this account has
    /// ever held, open or fully closed — each position's own
    /// <c>RealizedPnl</c> never resets, so this is the account-level total.
    /// </summary>
    Task<decimal> SumLifetimeRealizedPnlAsync(TradingAccountId tradingAccountId, CancellationToken cancellationToken);
}

/// <summary>Access to the one cash balance row per trading account.</summary>
public interface IPortfolioAccountRepository
{
    void Add(PortfolioAccount account);

    Task<PortfolioAccount?> GetAsync(TradingAccountId tradingAccountId, CancellationToken cancellationToken);
}

/// <summary>Access to the queryable history of completed round trips.</summary>
public interface ITradeRepository
{
    void Add(Trade trade);

    /// <summary>Sum of realised P&amp;L for every trade this account closed at or after <paramref name="sinceUtc"/>.</summary>
    Task<decimal> SumRealizedPnlSinceAsync(
        TradingAccountId tradingAccountId, DateTimeOffset sinceUtc, CancellationToken cancellationToken);
}

/// <summary>
/// Supplies the current live price for a symbol, for marking unrealised P&amp;L
/// on the read side.
/// </summary>
/// <remarks>
/// Deliberately separate from the fill-time mark used when a
/// <c>PositionUpdated</c> event is raised (that one uses the fill's own
/// price, so the published figure is reproducible from the event alone). A
/// read query wants the freshest price instead, and falls back to the
/// position's own average entry price when none is cached — see
/// <c>GetPositionsQueryHandler</c>.
/// </remarks>
public interface IMarkPriceProvider
{
    Task<decimal?> GetMarkPriceAsync(string symbol, CancellationToken cancellationToken);
}
