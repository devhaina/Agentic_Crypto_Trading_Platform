using Agentiva.BuildingBlocks.Domain.Primitives;
using Agentiva.Portfolio.Application.Abstractions;
using Agentiva.Portfolio.Domain.Accounts;
using Agentiva.Portfolio.Domain.Positions;
using Agentiva.Portfolio.Domain.Trades;
using Microsoft.EntityFrameworkCore;

namespace Agentiva.Portfolio.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IPositionRepository"/>.</summary>
public sealed class PositionRepository(PortfolioDbContext context) : IPositionRepository
{
    public void Add(Position position) => context.Positions.Add(position);

    public Task<Position?> GetAsync(TradingAccountId tradingAccountId, Symbol symbol, CancellationToken cancellationToken)
        => context.Positions.FirstOrDefaultAsync(
            p => p.TradingAccountId == tradingAccountId && p.Symbol == symbol, cancellationToken);

    public async Task<IReadOnlyList<Position>> ListOpenAsync(
        TradingAccountId tradingAccountId, CancellationToken cancellationToken)
        => await context.Positions
            .Where(p => p.TradingAccountId == tradingAccountId && p.Direction != PositionDirection.Flat)
            .ToListAsync(cancellationToken);

    public Task<decimal> SumLifetimeRealizedPnlAsync(TradingAccountId tradingAccountId, CancellationToken cancellationToken)
        => context.Positions
            .Where(p => p.TradingAccountId == tradingAccountId)
            .SumAsync(p => p.RealizedPnl, cancellationToken);
}

/// <summary>EF Core implementation of <see cref="IPortfolioAccountRepository"/>.</summary>
public sealed class PortfolioAccountRepository(PortfolioDbContext context) : IPortfolioAccountRepository
{
    public void Add(PortfolioAccount account) => context.Accounts.Add(account);

    public Task<PortfolioAccount?> GetAsync(TradingAccountId tradingAccountId, CancellationToken cancellationToken)
        => context.Accounts.FirstOrDefaultAsync(a => a.TradingAccountId == tradingAccountId, cancellationToken);
}

/// <summary>EF Core implementation of <see cref="ITradeRepository"/>.</summary>
public sealed class TradeRepository(PortfolioDbContext context) : ITradeRepository
{
    public void Add(Trade trade) => context.Trades.Add(trade);

    public Task<decimal> SumRealizedPnlSinceAsync(
        TradingAccountId tradingAccountId, DateTimeOffset sinceUtc, CancellationToken cancellationToken)
        => context.Trades
            .Where(t => t.TradingAccountId == tradingAccountId && t.ClosedAt >= sinceUtc)
            .SumAsync(t => t.RealizedPnl, cancellationToken);
}
