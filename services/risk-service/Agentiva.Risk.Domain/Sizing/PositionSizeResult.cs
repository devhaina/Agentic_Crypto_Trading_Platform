using Agentiva.BuildingBlocks.Domain.Primitives;

namespace Agentiva.Risk.Domain.Sizing;

/// <summary>Which constraint determined the final position size.</summary>
/// <remarks>
/// Recorded so that an operator seeing an unexpectedly small order can tell at
/// a glance whether it was the risk budget, a hard cap, the account balance or
/// an exchange filter that bound it — without re-deriving the arithmetic.
/// </remarks>
public enum SizingConstraint
{
    /// <summary>Size came from the risk budget and the stop distance.</summary>
    RiskBudget = 1,

    /// <summary>Capped by the policy's maximum notional per position.</summary>
    MaxPositionNotional = 2,

    /// <summary>Capped by available balance.</summary>
    AvailableBalance = 3,

    /// <summary>Capped by remaining portfolio exposure headroom.</summary>
    PortfolioExposure = 4,

    /// <summary>Capped by remaining per-asset concentration headroom.</summary>
    AssetConcentration = 5,

    /// <summary>Rounded to zero, or below an exchange minimum.</summary>
    BelowExchangeMinimum = 6
}

/// <summary>Outcome of a deterministic position sizing calculation.</summary>
/// <param name="Quantity">
/// Final quantity, normalised to the exchange step size. Zero means no
/// tradeable size survived the constraints.
/// </param>
/// <param name="EffectiveEntryPrice">Entry price including the adverse slippage assumption.</param>
/// <param name="EffectiveStopPrice">Stop price including the adverse slippage assumption.</param>
/// <param name="Notional">Order value in quote asset at the effective entry price.</param>
/// <param name="RiskAmount">
/// Capital at risk if the stop is hit, in quote asset, including both legs' fees.
/// </param>
/// <param name="RiskBudget">The risk budget the size was derived from.</param>
/// <param name="EstimatedEntryFee">Expected fee on the entry leg.</param>
/// <param name="EstimatedExitFee">Expected fee on the exit leg if the stop is hit.</param>
/// <param name="BindingConstraint">Which constraint determined the size.</param>
/// <param name="IsTradeable">Whether the result is a size that can actually be sent.</param>
public sealed record PositionSizeResult(
    Quantity Quantity,
    Price EffectiveEntryPrice,
    Price EffectiveStopPrice,
    Money Notional,
    Money RiskAmount,
    Money RiskBudget,
    Money EstimatedEntryFee,
    Money EstimatedExitFee,
    SizingConstraint BindingConstraint,
    bool IsTradeable);
