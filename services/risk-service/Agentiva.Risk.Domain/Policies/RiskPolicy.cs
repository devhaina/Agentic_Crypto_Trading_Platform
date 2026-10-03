using Agentiva.BuildingBlocks.Domain.Abstractions;
using Agentiva.BuildingBlocks.Domain.Exceptions;
using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Risk.Domain.Policies;

/// <summary>
/// The deterministic limits every trade is measured against.
/// </summary>
/// <remarks>
/// <para>
/// Stored in the Risk Service's own database, deliberately separate from any AI
/// or strategy configuration. The separation is the control: if the limits lived
/// alongside the agent configuration, a change to agent behaviour could alter
/// the constraints on that behaviour, and the risk gate would no longer be
/// independent of the thing it gates.
/// </para>
/// <para>
/// Every field is a percentage or an absolute amount with an explicit type, so
/// a limit cannot be misread by a factor of a hundred. See
/// <see cref="Percentage"/>.
/// </para>
/// </remarks>
public sealed class RiskPolicy : AggregateRoot<RiskPolicyId>
{
    private RiskPolicy()
    {
        // EF Core materialisation.
    }

    private RiskPolicy(RiskPolicyId id, string name, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Human-readable policy name, e.g. <c>default-conservative</c>.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Whether this is the policy applied when none is specified.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Whether this policy may be used at all.</summary>
    public bool IsActive { get; private set; } = true;

    // --- Per-trade limits ---------------------------------------------------

    /// <summary>Maximum fraction of equity that may be lost on one trade.</summary>
    public Percentage MaxRiskPerTrade { get; private set; } = Percentage.FromPercent(0.5m);

    /// <summary>Maximum notional value of a single position, in quote asset.</summary>
    public Money MaxPositionNotional { get; private set; } = Money.Create(1000m, AssetCode.Usdt);

    // --- Portfolio limits ---------------------------------------------------

    /// <summary>Maximum realised plus unrealised loss in one UTC day, as a fraction of equity.</summary>
    public Percentage MaxDailyLoss { get; private set; } = Percentage.FromPercent(2m);

    /// <summary>Maximum combined notional of open positions, as a fraction of equity.</summary>
    public Percentage MaxPortfolioExposure { get; private set; } = Percentage.FromPercent(50m);

    /// <summary>Maximum notional in any one symbol, as a fraction of equity.</summary>
    public Percentage MaxAssetConcentration { get; private set; } = Percentage.FromPercent(25m);

    /// <summary>Maximum simultaneously open positions.</summary>
    public int MaxOpenPositions { get; private set; } = 5;

    // --- Signal quality -----------------------------------------------------

    /// <summary>Minimum confidence a signal or proposal must carry.</summary>
    public Percentage MinConfidence { get; private set; } = Percentage.FromPercent(60m);

    /// <summary>
    /// Maximum acceptable annualised volatility for the symbol.
    /// </summary>
    /// <remarks>
    /// A volatility ceiling stops the platform sizing a position during a
    /// dislocation, when the stop distance a strategy computed from recent
    /// candles no longer reflects how far price can travel in a minute.
    /// </remarks>
    public Percentage MaxVolatility { get; private set; } = Percentage.FromPercent(100m);

    /// <summary>Whether a stop-loss is mandatory. Default true.</summary>
    /// <remarks>
    /// With no stop, loss is unbounded and position size cannot be derived from
    /// a risk budget at all — the sizing formula has no denominator. Disabling
    /// this is possible but is an audited, administrator-only change.
    /// </remarks>
    public bool RequireStopLoss { get; private set; } = true;

    /// <summary>Whether a take-profit target is mandatory.</summary>
    public bool RequireTakeProfit { get; private set; } = true;

    // --- Execution assumptions ----------------------------------------------

    /// <summary>Adverse slippage assumed on entry and on a stop exit.</summary>
    public Percentage SlippageAssumption { get; private set; } = Percentage.FromPercent(0.05m);

    /// <summary>Taker fee rate assumed per leg.</summary>
    /// <remarks>
    /// The taker rate is assumed rather than the maker rate, because a market
    /// entry and a triggered stop are both taker fills. Assuming the cheaper
    /// maker rate would understate the cost of being stopped out.
    /// </remarks>
    public Percentage TakerFee { get; private set; } = Percentage.FromPercent(0.1m);

    /// <summary>How stale market data may be before trading on it is refused.</summary>
    public TimeSpan MarketDataStalenessThreshold { get; private set; } = TimeSpan.FromSeconds(30);

    // --- Audit --------------------------------------------------------------

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>User id of the last editor, or <c>SYSTEM</c> for the seeded default.</summary>
    public string UpdatedBy { get; private set; } = "SYSTEM";

    /// <summary>
    /// Creates the conservative default policy used when none is configured.
    /// </summary>
    /// <remarks>
    /// The defaults are intentionally cautious: half a percent of equity at risk
    /// per trade, two percent per day, half the portfolio deployable at once. A
    /// platform whose out-of-the-box defaults are aggressive will eventually be
    /// run with them.
    /// </remarks>
    public static RiskPolicy CreateDefault(DateTimeOffset now, AssetCode quoteAsset)
    {
        var policy = new RiskPolicy(RiskPolicyId.New(), "default-conservative", now)
        {
            IsDefault = true,
            IsActive = true,
            MaxRiskPerTrade = Percentage.FromPercent(0.5m),
            MaxPositionNotional = Money.Create(1000m, quoteAsset),
            MaxDailyLoss = Percentage.FromPercent(2m),
            MaxPortfolioExposure = Percentage.FromPercent(50m),
            MaxAssetConcentration = Percentage.FromPercent(25m),
            MaxOpenPositions = 5,
            MinConfidence = Percentage.FromPercent(60m),
            MaxVolatility = Percentage.FromPercent(100m),
            RequireStopLoss = true,
            RequireTakeProfit = true,
            SlippageAssumption = Percentage.FromPercent(0.05m),
            TakerFee = Percentage.FromPercent(0.1m),
            MarketDataStalenessThreshold = TimeSpan.FromSeconds(30)
        };

        return policy;
    }

    /// <summary>Creates a policy from explicit limits.</summary>
    /// <exception cref="DomainException">A limit is internally inconsistent.</exception>
    public static RiskPolicy Create(
        string name,
        Percentage maxRiskPerTrade,
        Money maxPositionNotional,
        Percentage maxDailyLoss,
        Percentage maxPortfolioExposure,
        Percentage maxAssetConcentration,
        int maxOpenPositions,
        Percentage minConfidence,
        Percentage maxVolatility,
        bool requireStopLoss,
        bool requireTakeProfit,
        Percentage slippageAssumption,
        Percentage takerFee,
        TimeSpan marketDataStalenessThreshold,
        DateTimeOffset now,
        string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("risk.policy.name_required", "A risk policy must be named.");
        }

        if (maxOpenPositions is < 1 or > 100)
        {
            throw new DomainException(
                "risk.policy.invalid_max_open_positions",
                $"Maximum open positions must be between 1 and 100 but was {maxOpenPositions}.");
        }

        // A per-trade risk above the daily loss limit is self-contradictory: a
        // single losing trade would breach the day's budget, so the limits would
        // never both hold. Caught here rather than discovered in production.
        if (maxRiskPerTrade > maxDailyLoss)
        {
            throw new DomainException(
                "risk.policy.inconsistent_risk_limits",
                $"Maximum risk per trade ({maxRiskPerTrade}) exceeds the maximum daily loss "
                + $"({maxDailyLoss}); a single losing trade would breach the daily limit.");
        }

        // Concentration above total exposure can never bind, which makes it a
        // misleading setting rather than a harmless one.
        if (maxAssetConcentration > maxPortfolioExposure)
        {
            throw new DomainException(
                "risk.policy.inconsistent_exposure_limits",
                $"Maximum asset concentration ({maxAssetConcentration}) exceeds maximum portfolio "
                + $"exposure ({maxPortfolioExposure}) and could never take effect.");
        }

        if (marketDataStalenessThreshold <= TimeSpan.Zero
            || marketDataStalenessThreshold > TimeSpan.FromMinutes(10))
        {
            throw new DomainException(
                "risk.policy.invalid_staleness_threshold",
                "The market data staleness threshold must be between zero and ten minutes.");
        }

        return new RiskPolicy(RiskPolicyId.New(), name.Trim(), now)
        {
            MaxRiskPerTrade = maxRiskPerTrade,
            MaxPositionNotional = maxPositionNotional,
            MaxDailyLoss = maxDailyLoss,
            MaxPortfolioExposure = maxPortfolioExposure,
            MaxAssetConcentration = maxAssetConcentration,
            MaxOpenPositions = maxOpenPositions,
            MinConfidence = minConfidence,
            MaxVolatility = maxVolatility,
            RequireStopLoss = requireStopLoss,
            RequireTakeProfit = requireTakeProfit,
            SlippageAssumption = slippageAssumption,
            TakerFee = takerFee,
            MarketDataStalenessThreshold = marketDataStalenessThreshold,
            UpdatedBy = updatedBy
        };
    }

    /// <summary>Marks this policy as the default, deactivating nothing else.</summary>
    public void MarkAsDefault(DateTimeOffset now, string updatedBy)
    {
        IsDefault = true;
        Touch(now, updatedBy);
    }

    /// <summary>Deactivates the policy so it can no longer be applied.</summary>
    /// <exception cref="DomainException">The policy is the current default.</exception>
    public void Deactivate(DateTimeOffset now, string updatedBy)
    {
        // Deactivating the default would leave the platform with no limits to
        // apply, and "no policy" must never mean "no limits".
        if (IsDefault)
        {
            throw new DomainException(
                "risk.policy.cannot_deactivate_default",
                "The default risk policy cannot be deactivated. Promote another policy first.");
        }

        IsActive = false;
        Touch(now, updatedBy);
    }

    private void Touch(DateTimeOffset now, string updatedBy)
    {
        UpdatedAt = now;
        UpdatedBy = updatedBy;
    }
}
