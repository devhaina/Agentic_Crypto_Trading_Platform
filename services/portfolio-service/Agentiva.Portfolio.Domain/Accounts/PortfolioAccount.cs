using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Portfolio.Domain.Accounts;

/// <summary>
/// One trading account's quote-asset cash balance.
/// </summary>
/// <remarks>
/// There is no deposit or withdrawal flow anywhere in the platform yet, so
/// <see cref="Create"/> seeds the balance from a configured starting figure —
/// the same role <c>PaperPortfolioOptions.StartingEquity</c> played before
/// this service existed, just moved to where the balance is now real. From
/// that point on every change is a fill, never an operator override.
/// </remarks>
public sealed class PortfolioAccount : AggregateRoot<PortfolioId>
{
    private PortfolioAccount()
    {
        // EF Core materialisation.
    }

    private PortfolioAccount(PortfolioId id)
        : base(id)
    {
    }

    public TradingAccountId TradingAccountId { get; private set; }

    /// <summary>Unencumbered cash available to open new positions.</summary>
    public decimal CashBalance { get; private set; }

    /// <summary>
    /// Cash reserved against resting limit orders. Always zero today — every
    /// order this platform places is a market order that resolves
    /// immediately or is rejected — but carried so the shape matches
    /// <see cref="Contracts.Events.Portfolio.BalanceUpdated"/> once a limit
    /// order can rest.
    /// </summary>
    public decimal LockedBalance { get; private set; }

    public string QuoteAsset { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PortfolioAccount Create(
        TradingAccountId tradingAccountId, decimal startingCashBalance, string quoteAsset, DateTimeOffset now)
        => new(PortfolioId.New())
        {
            TradingAccountId = tradingAccountId,
            CashBalance = startingCashBalance,
            QuoteAsset = quoteAsset,
            CreatedAt = now,
            UpdatedAt = now
        };

    /// <summary>
    /// Applies one fill's cash effect: a buy spends notional plus fee, a sell
    /// receives notional minus fee. Never rejects a fill for insufficient
    /// cash — this aggregate records what the exchange already did, and the
    /// risk gate is where overspending is supposed to have been refused.
    /// </summary>
    public void ApplyFill(OrderSide side, decimal notional, decimal feePaid, string feeAsset, DateTimeOffset now)
    {
        // A fee in an asset other than the quote asset (a BNB-discounted fee)
        // is not converted and so does not move this balance — the same
        // simplification as Position.ApplyFill.
        var feeInQuote = string.Equals(feeAsset, QuoteAsset, StringComparison.OrdinalIgnoreCase) ? feePaid : 0m;

        CashBalance += side == OrderSide.Buy ? -(notional + feeInQuote) : notional - feeInQuote;
        UpdatedAt = now;

        Raise(new BalanceUpdatedDomainEvent(now, TradingAccountId.Value, QuoteAsset, CashBalance, LockedBalance));
    }
}
